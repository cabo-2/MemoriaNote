#!/usr/bin/env python3
"""Create reproducible Memoria Note self-contained release packages."""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import platform
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import zipfile
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Callable, Iterable, Sequence


SCHEMA_VERSION = 1
SUPPORTED_RIDS = ("linux-x64", "win-x64")
RELEASE_TAG_PATTERN = re.compile(
    r"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"
)
ZIP_TIMESTAMP = (1980, 1, 1, 0, 0, 0)
DIRECTORY_MODE = 0o755
FILE_MODE = 0o644
EXECUTABLE_MODE = 0o755


@dataclass(frozen=True)
class ReleaseArtifact:
    rid: str
    publish_directory: str
    executable: str
    archive: str
    archive_root: str
    sha256: str
    version_check: str


@dataclass(frozen=True)
class ArchiveEntry:
    source: Path | None
    archive_path: str
    is_directory: bool
    mode: int


CommandRunner = Callable[[Sequence[str], Path], subprocess.CompletedProcess[str]]


def parse_arguments(arguments: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Publish and package reproducible Memoria Note self-contained "
            "release archives without contacting the GitHub Release API."
        )
    )
    parser.add_argument(
        "--tag",
        required=True,
        help="Release tag in strict v<major>.<minor>.<patch> form.",
    )
    parser.add_argument(
        "--rid",
        action="append",
        choices=SUPPORTED_RIDS,
        dest="rids",
        help=(
            "Runtime identifier to package. Repeat to package both supported "
            "RIDs (default: both)."
        ),
    )
    parser.add_argument(
        "--output-directory",
        default="artifacts/release",
        help="Release output root (default: artifacts/release).",
    )
    parser.add_argument(
        "--repository-root",
        help="Repository root (default: inferred from this script).",
    )
    parser.add_argument(
        "--dotnet",
        default=os.environ.get("DOTNET_HOST_PATH", "dotnet"),
        help="dotnet host to execute (default: DOTNET_HOST_PATH or dotnet).",
    )
    return parser.parse_args(arguments)


def normalize_release_tag(tag: str) -> str:
    match = RELEASE_TAG_PATTERN.fullmatch(tag)
    if match is None:
        raise ValueError(
            "release tag must use v<major>.<minor>.<patch> with no leading zeros"
        )
    return ".".join(match.groups())


def repository_root_from_script() -> Path:
    return Path(__file__).resolve().parents[2]


def resolve_output_directory(repository_root: Path, value: str) -> Path:
    path = Path(value)
    if not path.is_absolute():
        path = repository_root / path
    return path.resolve()


def archive_name(tag: str, rid: str) -> str:
    extension = ".tar.gz" if rid == "linux-x64" else ".zip"
    return f"mn-{tag}-{rid}{extension}"


def archive_root_name(tag: str, rid: str) -> str:
    return f"mn-{tag}-{rid}"


def executable_name(rid: str) -> str:
    return "mn.exe" if rid == "win-x64" else "mn"


def build_publish_command(
    *,
    dotnet: str,
    project: Path,
    rid: str,
    version: str,
    output_directory: Path,
) -> list[str]:
    return [
        dotnet,
        "publish",
        str(project),
        "--configuration",
        "Release",
        "--runtime",
        rid,
        "--self-contained",
        "true",
        "--output",
        str(output_directory),
        "--nologo",
        f"-p:MemoriaNoteProductVersion={version}",
        "-p:MemoriaNoteReleaseBuild=true",
        "-p:ContinuousIntegrationBuild=true",
    ]


def default_command_runner(
    command: Sequence[str], working_directory: Path
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=working_directory,
        check=False,
        capture_output=True,
        text=True,
    )


def run_checked_command(
    command: Sequence[str],
    working_directory: Path,
    runner: CommandRunner,
) -> subprocess.CompletedProcess[str]:
    completed = runner(command, working_directory)
    if completed.returncode != 0:
        command_text = subprocess.list2cmdline(list(command))
        raise RuntimeError(
            f"command failed with exit code {completed.returncode}: {command_text}\n"
            f"stdout:\n{completed.stdout}\n"
            f"stderr:\n{completed.stderr}"
        )
    return completed


def clean_directory(path: Path) -> None:
    if path.is_symlink():
        raise ValueError(f"refusing to clean a symbolic link: {path}")
    if path.exists():
        if not path.is_dir():
            raise ValueError(f"output path is not a directory: {path}")
        shutil.rmtree(path)
    path.mkdir(parents=True)


def validate_repository(repository_root: Path) -> tuple[Path, Path]:
    project = repository_root / "cli" / "mn.csproj"
    license_path = repository_root / "LICENSE"
    missing = [path for path in (project, license_path) if not path.is_file()]
    if missing:
        paths = ", ".join(str(path) for path in missing)
        raise ValueError(f"repository root is missing required files: {paths}")
    return project, license_path


def validate_publish_output(publish_directory: Path, rid: str) -> Path:
    executable = publish_directory / executable_name(rid)
    required = (
        executable,
        publish_directory / "mn.dll",
        publish_directory / "mn.deps.json",
        publish_directory / "mn.runtimeconfig.json",
    )
    missing = [path.name for path in required if not path.is_file()]
    if missing:
        raise RuntimeError(
            f"publish output for {rid} is missing required files: "
            + ", ".join(missing)
        )
    if rid == "linux-x64":
        executable.chmod(
            executable.stat().st_mode
            | stat.S_IXUSR
            | stat.S_IXGRP
            | stat.S_IXOTH
        )
    return executable


def validate_published_version(
    *,
    dotnet: str,
    publish_directory: Path,
    version: str,
    repository_root: Path,
    runner: CommandRunner,
) -> None:
    completed = run_checked_command(
        (dotnet, str(publish_directory / "mn.dll"), "--version"),
        repository_root,
        runner,
    )
    expected = f"mn {version}\n"
    if completed.stdout.replace("\r\n", "\n") != expected or completed.stderr:
        raise RuntimeError(
            "published CLI version contract did not match the normalized tag\n"
            f"expected stdout: {expected!r}\n"
            f"actual stdout: {completed.stdout!r}\n"
            f"actual stderr: {completed.stderr!r}"
        )


def host_can_execute(
    rid: str,
    *,
    system: str | None = None,
    machine: str | None = None,
) -> bool:
    host_system = (system or platform.system()).lower()
    host_machine = (machine or platform.machine()).lower()
    is_x64 = host_machine in ("amd64", "x86_64")
    return is_x64 and (
        (rid == "linux-x64" and host_system == "linux")
        or (rid == "win-x64" and host_system == "windows")
    )


def collect_archive_entries(
    *,
    publish_directory: Path,
    license_path: Path,
    archive_root: str,
    executable: Path,
) -> list[ArchiveEntry]:
    entries = [ArchiveEntry(None, archive_root, True, DIRECTORY_MODE)]
    for source in sorted(publish_directory.rglob("*"), key=lambda path: path.as_posix()):
        if source.is_symlink():
            raise ValueError(f"publish output contains a symbolic link: {source}")
        relative = source.relative_to(publish_directory).as_posix()
        archive_path = f"{archive_root}/{relative}"
        if source.is_dir():
            entries.append(
                ArchiveEntry(source, archive_path, True, DIRECTORY_MODE)
            )
        elif source.is_file():
            mode = EXECUTABLE_MODE if source == executable else FILE_MODE
            entries.append(ArchiveEntry(source, archive_path, False, mode))
        else:
            raise ValueError(f"publish output contains an unsupported entry: {source}")
    entries.append(
        ArchiveEntry(
            license_path,
            f"{archive_root}/LICENSE",
            False,
            FILE_MODE,
        )
    )
    return sorted(entries, key=lambda entry: entry.archive_path)


def create_tar_gz(path: Path, entries: Iterable[ArchiveEntry]) -> None:
    with path.open("wb") as destination:
        with gzip.GzipFile(
            filename="", mode="wb", fileobj=destination, compresslevel=9, mtime=0
        ) as compressed:
            with tarfile.open(
                fileobj=compressed,
                mode="w",
                format=tarfile.PAX_FORMAT,
            ) as archive:
                for entry in sorted(entries, key=lambda item: item.archive_path):
                    info = tarfile.TarInfo(entry.archive_path)
                    info.mode = entry.mode
                    info.mtime = 0
                    info.uid = 0
                    info.gid = 0
                    info.uname = ""
                    info.gname = ""
                    if entry.is_directory:
                        info.type = tarfile.DIRTYPE
                        info.size = 0
                        archive.addfile(info)
                    else:
                        if entry.source is None:
                            raise ValueError(
                                f"file entry has no source: {entry.archive_path}"
                            )
                        info.type = tarfile.REGTYPE
                        info.size = entry.source.stat().st_size
                        with entry.source.open("rb") as source:
                            archive.addfile(info, source)


def create_zip(path: Path, entries: Iterable[ArchiveEntry]) -> None:
    with zipfile.ZipFile(
        path,
        mode="w",
        compression=zipfile.ZIP_DEFLATED,
        compresslevel=9,
        strict_timestamps=True,
    ) as archive:
        for entry in sorted(entries, key=lambda item: item.archive_path):
            name = entry.archive_path + ("/" if entry.is_directory else "")
            info = zipfile.ZipInfo(name, date_time=ZIP_TIMESTAMP)
            info.create_system = 3
            info.compress_type = zipfile.ZIP_DEFLATED
            file_type = stat.S_IFDIR if entry.is_directory else stat.S_IFREG
            info.external_attr = (file_type | entry.mode) << 16
            if entry.is_directory:
                info.external_attr |= 0x10
                archive.writestr(info, b"")
            else:
                if entry.source is None:
                    raise ValueError(
                        f"file entry has no source: {entry.archive_path}"
                    )
                archive.writestr(info, entry.source.read_bytes())


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_checksum_file(path: Path, artifacts: Sequence[ReleaseArtifact]) -> None:
    lines = [
        f"{artifact.sha256}  {Path(artifact.archive).name}\n"
        for artifact in sorted(artifacts, key=lambda item: Path(item.archive).name)
    ]
    path.write_text("".join(lines), encoding="utf-8", newline="\n")


def package_release(
    *,
    tag: str,
    rids: Sequence[str],
    output_directory: Path,
    repository_root: Path,
    dotnet: str,
    runner: CommandRunner = default_command_runner,
) -> dict[str, object]:
    version = normalize_release_tag(tag)
    if not rids:
        raise ValueError("at least one runtime identifier is required")
    if len(set(rids)) != len(rids):
        raise ValueError("runtime identifiers must not be repeated")
    unsupported = sorted(set(rids) - set(SUPPORTED_RIDS))
    if unsupported:
        raise ValueError(f"unsupported runtime identifier: {', '.join(unsupported)}")

    repository_root = repository_root.resolve()
    project, license_path = validate_repository(repository_root)
    release_directory = output_directory.resolve() / tag
    clean_directory(release_directory)

    artifacts: list[ReleaseArtifact] = []
    for rid in rids:
        publish_directory = release_directory / "publish" / rid
        publish_directory.mkdir(parents=True)
        command = build_publish_command(
            dotnet=dotnet,
            project=project,
            rid=rid,
            version=version,
            output_directory=publish_directory,
        )
        print(f"Publishing {rid} for {tag}...", file=sys.stderr)
        run_checked_command(command, repository_root, runner)
        executable = validate_publish_output(publish_directory, rid)
        if host_can_execute(rid):
            validate_published_version(
                dotnet=dotnet,
                publish_directory=publish_directory,
                version=version,
                repository_root=repository_root,
                runner=runner,
            )
            version_check = "executed"
        else:
            version_check = "deferred-to-native-host"
            print(
                f"Deferring {rid} version execution check to a native host.",
                file=sys.stderr,
            )

        root_name = archive_root_name(tag, rid)
        archive_path = release_directory / archive_name(tag, rid)
        entries = collect_archive_entries(
            publish_directory=publish_directory,
            license_path=license_path,
            archive_root=root_name,
            executable=executable,
        )
        if rid == "linux-x64":
            create_tar_gz(archive_path, entries)
        else:
            create_zip(archive_path, entries)

        artifacts.append(
            ReleaseArtifact(
                rid=rid,
                publish_directory=str(publish_directory.resolve()),
                executable=str(executable.resolve()),
                archive=str(archive_path.resolve()),
                archive_root=root_name,
                sha256=sha256(archive_path),
                version_check=version_check,
            )
        )

    checksum_file = release_directory / "SHA256SUMS"
    write_checksum_file(checksum_file, artifacts)
    result_file = release_directory / "package-results.json"
    result: dict[str, object] = {
        "schema_version": SCHEMA_VERSION,
        "tag": tag,
        "version": version,
        "output_directory": str(release_directory.resolve()),
        "checksum_file": str(checksum_file.resolve()),
        "result_file": str(result_file.resolve()),
        "artifacts": [asdict(artifact) for artifact in artifacts],
    }
    serialized = json.dumps(result, indent=2, sort_keys=True) + "\n"
    result_file.write_text(serialized, encoding="utf-8", newline="\n")
    print(serialized, end="")
    return result


def main(arguments: Sequence[str] | None = None) -> int:
    parsed = parse_arguments(sys.argv[1:] if arguments is None else arguments)
    try:
        repository_root = (
            Path(parsed.repository_root).resolve()
            if parsed.repository_root
            else repository_root_from_script()
        )
        output_directory = resolve_output_directory(
            repository_root, parsed.output_directory
        )
        package_release(
            tag=parsed.tag,
            rids=parsed.rids or SUPPORTED_RIDS,
            output_directory=output_directory,
            repository_root=repository_root,
            dotnet=parsed.dotnet,
        )
    except (OSError, RuntimeError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
