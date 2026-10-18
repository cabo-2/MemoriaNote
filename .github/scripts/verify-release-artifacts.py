#!/usr/bin/env python3
"""Verify published Memoria Note executables and release archives."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Callable, Sequence


SCHEMA_VERSION = 1
SUPPORTED_RIDS = ("linux-x64", "win-x64")
DEFAULT_TIMEOUT_SECONDS = 20.0
EDITOR_TEXT_ENVIRONMENT_VARIABLE = "MEMORIA_NOTE_ARTIFACT_EDITOR_TEXT"


class VerificationError(RuntimeError):
    """Reports an artifact contract failure."""


@dataclass(frozen=True)
class CommandResult:
    arguments: tuple[str, ...]
    exit_code: int
    standard_output: bytes
    standard_error: bytes


@dataclass(frozen=True)
class PackageArtifact:
    tag: str
    version: str
    rid: str
    publish_directory: Path
    executable: Path
    archive: Path
    archive_root: str
    checksum_file: Path
    sha256: str


@dataclass(frozen=True)
class ArchiveMember:
    name: str
    is_directory: bool
    mode: int


SmokeRunner = Callable[[Path, str, str, Path, float], None]


def parse_arguments(arguments: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Run black-box smoke scenarios against explicit published executables "
            "and optionally verify an NCLI-650b release package."
        )
    )
    subparsers = parser.add_subparsers(dest="mode", required=True)

    smoke = subparsers.add_parser(
        "smoke",
        help="Run the shared smoke scenarios against one explicit executable.",
    )
    add_common_options(smoke)
    smoke.add_argument(
        "--executable",
        required=True,
        help="Absolute path of the published executable to verify.",
    )

    package = subparsers.add_parser(
        "package",
        help=(
            "Verify one package result, then smoke-test its publish directory "
            "and clean archive extraction."
        ),
    )
    add_common_options(package)
    package.add_argument(
        "--package-results",
        required=True,
        help="Path to package-results.json produced by package-release.py.",
    )
    package.add_argument(
        "--publish-executable",
        required=True,
        help=(
            "Explicit publish executable path; it must identify the same file "
            "recorded in package-results.json."
        ),
    )
    return parser.parse_args(arguments)


def add_common_options(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--rid",
        required=True,
        choices=SUPPORTED_RIDS,
        help="Runtime identifier of the executable under test.",
    )
    parser.add_argument(
        "--expected-version",
        required=True,
        help="Expected suffix-free product version, such as 0.2.0.",
    )
    parser.add_argument(
        "--temporary-root",
        help="Existing directory beneath which isolated verification state is created.",
    )
    parser.add_argument(
        "--timeout-seconds",
        type=positive_number,
        default=DEFAULT_TIMEOUT_SECONDS,
        help=(
            "Timeout applied to each CLI command "
            f"(default: {DEFAULT_TIMEOUT_SECONDS:g})."
        ),
    )
    parser.add_argument(
        "--cleanup-on-failure",
        action="store_true",
        help="Delete temporary verification state after a failure.",
    )


def positive_number(value: str) -> float:
    parsed = float(value)
    if parsed <= 0:
        raise argparse.ArgumentTypeError("must be greater than zero")
    return parsed


def validate_expected_version(version: str) -> None:
    components = version.split(".")
    if len(components) != 3 or any(
        not component.isdigit()
        or (len(component) > 1 and component.startswith("0"))
        for component in components
    ):
        raise VerificationError(
            "expected version must use <major>.<minor>.<patch> with no leading zeros"
        )


def resolve_existing_file(value: str, description: str) -> Path:
    path = Path(value).resolve()
    if not path.is_file():
        raise VerificationError(f"{description} does not exist: {path}")
    return path


def resolve_temporary_root(value: str | None) -> Path | None:
    if value is None:
        return None
    path = Path(value).resolve()
    if not path.is_dir():
        raise VerificationError(f"temporary root does not exist: {path}")
    return path


def create_verification_root(parent: Path | None) -> Path:
    return Path(
        tempfile.mkdtemp(
            prefix="MemoriaNote artifact smoke 日本語 space-",
            dir=parent,
        )
    ).resolve()


def execute_with_temporary_root(
    *,
    parent: Path | None,
    cleanup_on_failure: bool,
    action: Callable[[Path], None],
) -> None:
    root = create_verification_root(parent)
    try:
        action(root)
    except Exception as error:
        if cleanup_on_failure:
            shutil.rmtree(root, ignore_errors=True)
            raise
        raise VerificationError(
            f"{error}\nTemporary verification state preserved at: {root}"
        ) from error
    else:
        shutil.rmtree(root)


def isolated_environment(root: Path) -> dict[str, str]:
    application_data_root = root / "application data"
    application_data_directory = application_data_root / "MemoriaNote"
    temporary_directory = root / "temporary files"
    dotnet_home = root / "dotnet home"
    user_home = root / "user home"
    for directory in (
        application_data_root,
        temporary_directory,
        dotnet_home,
        user_home,
    ):
        directory.mkdir(parents=True)

    environment = os.environ.copy()
    environment.update(
        {
            "APPDATA": str(application_data_root),
            "XDG_CONFIG_HOME": str(application_data_root),
            "MEMORIA_NOTE_APPLICATION_DATA_DIRECTORY": str(
                application_data_directory
            ),
            "DOTNET_CLI_HOME": str(dotnet_home),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_NOLOGO": "1",
            "TMPDIR": str(temporary_directory),
            "TMP": str(temporary_directory),
            "TEMP": str(temporary_directory),
            "HOME": str(user_home),
            "USERPROFILE": str(user_home),
        }
    )
    environment.pop("EDITOR", None)
    environment.pop("VISUAL", None)
    return environment


def run_command(
    *,
    executable: Path,
    arguments: Sequence[str],
    working_directory: Path,
    environment: dict[str, str],
    timeout_seconds: float,
) -> CommandResult:
    command = (str(executable), *arguments)
    try:
        completed = subprocess.run(
            command,
            cwd=working_directory,
            env=environment,
            check=False,
            capture_output=True,
            timeout=timeout_seconds,
        )
    except subprocess.TimeoutExpired as error:
        raise VerificationError(
            format_command_failure(
                command,
                f"timed out after {timeout_seconds:g} seconds",
                error.stdout or b"",
                error.stderr or b"",
            )
        ) from error
    except OSError as error:
        raise VerificationError(
            f"unable to start command {format_command(command)}: {error}"
        ) from error
    return CommandResult(
        arguments=command,
        exit_code=completed.returncode,
        standard_output=completed.stdout,
        standard_error=completed.stderr,
    )


def format_command(arguments: Sequence[str]) -> str:
    return subprocess.list2cmdline(list(arguments))


def format_command_failure(
    arguments: Sequence[str],
    problem: str,
    standard_output: bytes,
    standard_error: bytes,
) -> str:
    return (
        f"command {problem}: {format_command(arguments)}\n"
        f"stdout ({len(standard_output)} bytes):\n"
        f"{decode_for_diagnostic(standard_output)}\n"
        f"stderr ({len(standard_error)} bytes):\n"
        f"{decode_for_diagnostic(standard_error)}"
    )


def decode_for_diagnostic(value: bytes) -> str:
    return value.decode("utf-8", errors="backslashreplace")


def decode_utf8(value: bytes, stream: str, result: CommandResult) -> str:
    try:
        return value.decode("utf-8", errors="strict")
    except UnicodeDecodeError as error:
        raise VerificationError(
            format_command_failure(
                result.arguments,
                f"wrote invalid UTF-8 to {stream}",
                result.standard_output,
                result.standard_error,
            )
        ) from error


def require_exit_code(result: CommandResult, expected: int) -> None:
    if result.exit_code != expected:
        raise VerificationError(
            format_command_failure(
                result.arguments,
                f"returned exit code {result.exit_code}; expected {expected}",
                result.standard_output,
                result.standard_error,
            )
        )


def require_empty_stderr(result: CommandResult) -> None:
    if result.standard_error:
        raise VerificationError(
            format_command_failure(
                result.arguments,
                "wrote unexpected standard error",
                result.standard_output,
                result.standard_error,
            )
        )


def require_contains(result: CommandResult, value: str, stream: str = "stdout") -> None:
    raw = result.standard_output if stream == "stdout" else result.standard_error
    text = decode_utf8(raw, stream, result)
    if value not in text:
        raise VerificationError(
            format_command_failure(
                result.arguments,
                f"did not write {value!r} to {stream}",
                result.standard_output,
                result.standard_error,
            )
        )


def run_artifact_smoke(
    executable: Path,
    rid: str,
    expected_version: str,
    root: Path,
    timeout_seconds: float,
) -> None:
    validate_published_layout(executable, rid)
    workspace = root / "workspace 日本語 space"
    restore_workspace = root / "restored workspace 日本語"
    workspace.mkdir(parents=True)
    restore_workspace.mkdir(parents=True)
    environment = isolated_environment(root)

    def invoke(*arguments: str, cwd: Path = workspace) -> CommandResult:
        return run_command(
            executable=executable,
            arguments=arguments,
            working_directory=cwd,
            environment=environment,
            timeout_seconds=timeout_seconds,
        )

    version = invoke("--version")
    require_exit_code(version, 0)
    require_empty_stderr(version)
    expected_version_output = f"mn {expected_version}\n".encode("utf-8")
    if version.standard_output.replace(b"\r\n", b"\n") != expected_version_output:
        raise VerificationError(
            format_command_failure(
                version.arguments,
                f"did not report expected version {expected_version!r}",
                version.standard_output,
                version.standard_error,
            )
        )

    help_result = invoke("--help")
    require_exit_code(help_result, 0)
    require_empty_stderr(help_result)
    for text in ("Usage: mn", "create", "notebooks", "completion"):
        require_contains(help_result, text)

    invalid = invoke("--artifact-smoke-invalid-option")
    require_exit_code(invalid, 2)
    if invalid.standard_output:
        raise VerificationError(
            format_command_failure(
                invalid.arguments,
                "wrote parser diagnostics to standard output",
                invalid.standard_output,
                invalid.standard_error,
            )
        )
    require_contains(invalid, "Error:", stream="stderr")

    completion = invoke("completion", "zsh")
    require_exit_code(completion, 0)
    require_empty_stderr(completion)
    if (
        completion.standard_output.startswith(b"\xef\xbb\xbf")
        or b"\r" in completion.standard_output
        or not completion.standard_output.startswith(b"#compdef mn\n")
        or not completion.standard_output.endswith(b"\n")
    ):
        raise VerificationError(
            format_command_failure(
                completion.arguments,
                "violated the UTF-8, BOM-free, LF completion contract",
                completion.standard_output,
                completion.standard_error,
            )
        )
    for text in ("--workspace", "notebooks", "completion"):
        require_contains(completion, text)
    application_data = Path(
        environment["MEMORIA_NOTE_APPLICATION_DATA_DIRECTORY"]
    )
    if application_data.exists():
        raise VerificationError(
            "completion created application data before any stateful command: "
            f"{application_data}"
        )

    notebook_name = "artifact notebook 日本語.mnote"
    create = invoke("create", notebook_name)
    require_exit_code(create, 0)
    require_empty_stderr(create)
    notebook_path = workspace / notebook_name
    if not notebook_path.is_file():
        raise VerificationError(f"create did not produce notebook: {notebook_path}")

    use = invoke("use", "artifact notebook 日本語")
    require_exit_code(use, 0)
    require_empty_stderr(use)

    editor_helper = root / "artifact editor helper.py"
    editor_helper.write_text(
        """from pathlib import Path
import os
import sys

Path(sys.argv[-1]).write_text(
    os.environ[\"MEMORIA_NOTE_ARTIFACT_EDITOR_TEXT\"],
    encoding=\"utf-8\",
    newline=\"\\n\",
)
""",
        encoding="utf-8",
        newline="\n",
    )
    page_name = "Page 日本語"
    page_body = "Published artifact body 日本語 😀"
    environment[EDITOR_TEXT_ENVIRONMENT_VARIABLE] = page_body
    new_page = invoke(
        "new",
        page_name,
        "--editor",
        sys.executable,
        f"--editor-arg={editor_helper}",
        "--editor-arg={file}",
    )
    require_exit_code(new_page, 0)
    require_empty_stderr(new_page)
    require_contains(new_page, f'Created page "{page_name}"')

    listed = invoke("ls")
    require_exit_code(listed, 0)
    require_empty_stderr(listed)
    require_contains(listed, page_name)

    displayed = invoke("cat", page_name)
    require_exit_code(displayed, 0)
    require_empty_stderr(displayed)
    if displayed.standard_output.decode("utf-8", errors="strict") != page_body:
        raise VerificationError(
            format_command_failure(
                displayed.arguments,
                "did not preserve the page body",
                displayed.standard_output,
                displayed.standard_error,
            )
        )

    backup_path = workspace / "artifact backup 日本語.zip"
    backup = invoke("notebooks", "backup", str(backup_path))
    require_exit_code(backup, 0)
    require_empty_stderr(backup)
    if not backup_path.is_file():
        raise VerificationError(f"backup did not produce archive: {backup_path}")
    verify_backup_version(backup_path, expected_version)

    restore = invoke(
        "--workspace",
        str(restore_workspace),
        "notebooks",
        "restore",
        str(backup_path),
        "--target",
        "restored",
    )
    require_exit_code(restore, 0)
    require_empty_stderr(restore)
    restored_notebook = restore_workspace / "restored.mnote"
    if not restored_notebook.is_file():
        raise VerificationError(
            f"restore did not produce notebook: {restored_notebook}"
        )

    restored_page = invoke(
        "--workspace",
        str(restore_workspace),
        "cat",
        page_name,
        "--notebook",
        "restored",
    )
    require_exit_code(restored_page, 0)
    require_empty_stderr(restored_page)
    if restored_page.standard_output.decode("utf-8", errors="strict") != page_body:
        raise VerificationError(
            format_command_failure(
                restored_page.arguments,
                "did not preserve the restored page body",
                restored_page.standard_output,
                restored_page.standard_error,
            )
        )


def verify_backup_version(path: Path, expected_version: str) -> None:
    try:
        with zipfile.ZipFile(path) as archive:
            with archive.open("manifest.json") as manifest_file:
                manifest = json.load(manifest_file)
        actual = manifest["createdBy"]["version"]
    except (KeyError, OSError, ValueError, zipfile.BadZipFile) as error:
        raise VerificationError(f"unable to inspect backup manifest {path}: {error}") from error
    if actual != expected_version:
        raise VerificationError(
            f"backup creator version was {actual!r}; expected {expected_version!r}"
        )


def required_publish_files(rid: str) -> tuple[str, ...]:
    common = ("mn.dll", "mn.deps.json", "mn.runtimeconfig.json")
    if rid == "linux-x64":
        return (
            "mn",
            *common,
            "libhostfxr.so",
            "libhostpolicy.so",
            "libe_sqlite3.so",
        )
    return (
        "mn.exe",
        *common,
        "hostfxr.dll",
        "hostpolicy.dll",
        "e_sqlite3.dll",
    )


def validate_published_layout(executable: Path, rid: str) -> None:
    expected_name = "mn" if rid == "linux-x64" else "mn.exe"
    if executable.name != expected_name:
        raise VerificationError(
            f"{rid} executable must be named {expected_name!r}: {executable}"
        )
    if not executable.is_file():
        raise VerificationError(f"published executable does not exist: {executable}")
    missing = [
        name
        for name in required_publish_files(rid)
        if not (executable.parent / name).is_file()
    ]
    if missing:
        raise VerificationError(
            f"published {rid} layout is missing: {', '.join(missing)}"
        )
    if rid == "linux-x64" and executable.stat().st_mode & 0o111 == 0:
        raise VerificationError(f"Linux executable bit is not set: {executable}")


def read_package_artifact(
    *,
    package_results: Path,
    rid: str,
    expected_version: str,
    publish_executable: Path,
) -> PackageArtifact:
    try:
        data = json.loads(package_results.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise VerificationError(
            f"unable to read package results {package_results}: {error}"
        ) from error
    if data.get("schema_version") != SCHEMA_VERSION:
        raise VerificationError(
            f"unsupported package result schema: {data.get('schema_version')!r}"
        )
    version = data.get("version")
    tag = data.get("tag")
    if version != expected_version or tag != f"v{expected_version}":
        raise VerificationError(
            f"package version/tag was {version!r}/{tag!r}; expected "
            f"{expected_version!r}/'v{expected_version}'"
        )
    records = [
        item for item in data.get("artifacts", []) if item.get("rid") == rid
    ]
    if len(records) != 1:
        raise VerificationError(
            f"package results must contain exactly one {rid} artifact"
        )
    record = records[0]
    try:
        recorded_executable = Path(record["executable"]).resolve()
        artifact = PackageArtifact(
            tag=tag,
            version=version,
            rid=rid,
            publish_directory=Path(record["publish_directory"]).resolve(),
            executable=recorded_executable,
            archive=Path(record["archive"]).resolve(),
            archive_root=record["archive_root"],
            checksum_file=Path(data["checksum_file"]).resolve(),
            sha256=record["sha256"],
        )
    except (KeyError, TypeError) as error:
        raise VerificationError(
            f"package results contain an incomplete {rid} artifact"
        ) from error
    if not recorded_executable.is_file() or not publish_executable.is_file():
        raise VerificationError("publish executable is missing")
    if not os.path.samefile(recorded_executable, publish_executable):
        raise VerificationError(
            "explicit publish executable does not match package results: "
            f"{publish_executable} != {recorded_executable}"
        )
    if artifact.publish_directory != recorded_executable.parent:
        raise VerificationError(
            "package publish directory does not contain its recorded executable"
        )
    return artifact


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as source:
            for block in iter(lambda: source.read(1024 * 1024), b""):
                digest.update(block)
    except OSError as error:
        raise VerificationError(f"unable to hash {path}: {error}") from error
    return digest.hexdigest()


def verify_checksum(artifact: PackageArtifact) -> None:
    if not artifact.archive.is_file():
        raise VerificationError(f"release archive is missing: {artifact.archive}")
    if not artifact.checksum_file.is_file():
        raise VerificationError(
            f"release checksum file is missing: {artifact.checksum_file}"
        )
    actual = sha256(artifact.archive)
    if actual != artifact.sha256:
        raise VerificationError(
            f"archive SHA-256 was {actual}; package results expected {artifact.sha256}"
        )
    try:
        lines = artifact.checksum_file.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeDecodeError) as error:
        raise VerificationError(
            f"unable to read checksum file {artifact.checksum_file}: {error}"
        ) from error
    matches = []
    for line in lines:
        parts = line.split("  ", maxsplit=1)
        if len(parts) == 2 and parts[1] == artifact.archive.name:
            matches.append(parts[0])
    if matches != [actual]:
        raise VerificationError(
            f"checksum file must contain one matching entry for {artifact.archive.name}"
        )


def expected_archive_name(artifact: PackageArtifact) -> str:
    extension = ".tar.gz" if artifact.rid == "linux-x64" else ".zip"
    return f"mn-{artifact.tag}-{artifact.rid}{extension}"


def validate_member_name(name: str, expected_root: str) -> str:
    if "\\" in name or name.startswith("/"):
        raise VerificationError(f"archive contains unsafe path: {name!r}")
    normalized = name.rstrip("/")
    path = PurePosixPath(normalized)
    if not normalized or path.is_absolute() or ".." in path.parts:
        raise VerificationError(f"archive contains unsafe path: {name!r}")
    if normalized != path.as_posix():
        raise VerificationError(f"archive contains a non-normalized path: {name!r}")
    if not path.parts or path.parts[0] != expected_root:
        raise VerificationError(
            f"archive entry is outside the expected root {expected_root!r}: {name!r}"
        )
    return path.as_posix()


def inspect_tar_archive(path: Path, expected_root: str) -> list[ArchiveMember]:
    members: list[ArchiveMember] = []
    try:
        with tarfile.open(path, "r:gz") as archive:
            for member in archive.getmembers():
                name = validate_member_name(member.name, expected_root)
                if member.issym() or member.islnk():
                    raise VerificationError(
                        f"archive contains a symbolic or hard link: {member.name!r}"
                    )
                if not member.isdir() and not member.isfile():
                    raise VerificationError(
                        f"archive contains unsupported entry: {member.name!r}"
                    )
                members.append(
                    ArchiveMember(name, member.isdir(), stat.S_IMODE(member.mode))
                )
    except (OSError, tarfile.TarError) as error:
        raise VerificationError(f"unable to inspect tar archive {path}: {error}") from error
    return members


def inspect_zip_archive(path: Path, expected_root: str) -> list[ArchiveMember]:
    members: list[ArchiveMember] = []
    try:
        with zipfile.ZipFile(path) as archive:
            for info in archive.infolist():
                name = validate_member_name(info.filename, expected_root)
                mode = (info.external_attr >> 16) & 0xFFFF
                if stat.S_ISLNK(mode):
                    raise VerificationError(
                        f"archive contains a symbolic link: {info.filename!r}"
                    )
                members.append(
                    ArchiveMember(name, info.is_dir(), stat.S_IMODE(mode))
                )
    except (OSError, zipfile.BadZipFile) as error:
        raise VerificationError(f"unable to inspect ZIP archive {path}: {error}") from error
    return members


def inspect_archive(artifact: PackageArtifact) -> list[ArchiveMember]:
    expected_name = expected_archive_name(artifact)
    expected_root = f"mn-{artifact.tag}-{artifact.rid}"
    if artifact.archive.name != expected_name:
        raise VerificationError(
            f"archive name was {artifact.archive.name!r}; expected {expected_name!r}"
        )
    if artifact.archive_root != expected_root:
        raise VerificationError(
            f"archive root was {artifact.archive_root!r}; expected {expected_root!r}"
        )
    members = (
        inspect_tar_archive(artifact.archive, expected_root)
        if artifact.rid == "linux-x64"
        else inspect_zip_archive(artifact.archive, expected_root)
    )
    names = [member.name for member in members]
    if len(names) != len(set(names)):
        raise VerificationError("archive contains duplicate entry names")
    if names != sorted(names):
        raise VerificationError("archive entries are not sorted by path")
    member_by_name = {member.name: member for member in members}
    root = member_by_name.get(expected_root)
    if root is None or not root.is_directory:
        raise VerificationError("archive does not contain its single root directory")
    required = {
        f"{expected_root}/LICENSE",
        *(f"{expected_root}/{name}" for name in required_publish_files(artifact.rid)),
    }
    missing = sorted(required - set(names))
    if missing:
        raise VerificationError(
            "archive is missing required entries: " + ", ".join(missing)
        )
    if artifact.rid == "linux-x64":
        executable = member_by_name[f"{expected_root}/mn"]
        if executable.mode & 0o111 == 0:
            raise VerificationError("Linux archive executable bit is not set")
    expected_executable = f"{expected_root}/" + (
        "mn" if artifact.rid == "linux-x64" else "mn.exe"
    )
    for member in members:
        expected_mode = (
            0o755
            if member.is_directory or member.name == expected_executable
            else 0o644
        )
        if member.mode != expected_mode:
            raise VerificationError(
                f"archive entry {member.name!r} has mode {member.mode:04o}; "
                f"expected {expected_mode:04o}"
            )
    return members


def extract_archive(
    artifact: PackageArtifact,
    members: Sequence[ArchiveMember],
    destination: Path,
) -> Path:
    if destination.exists():
        raise VerificationError(f"archive extraction destination exists: {destination}")
    destination.mkdir(parents=True)
    member_by_name = {member.name: member for member in members}
    if artifact.rid == "linux-x64":
        extract_tar(artifact.archive, destination, member_by_name)
    else:
        extract_zip(artifact.archive, destination, member_by_name)
    return destination / artifact.archive_root


def extract_tar(
    archive_path: Path,
    destination: Path,
    members: dict[str, ArchiveMember],
) -> None:
    with tarfile.open(archive_path, "r:gz") as archive:
        for info in archive.getmembers():
            name = info.name.rstrip("/")
            member = members[name]
            target = destination.joinpath(*PurePosixPath(name).parts)
            if member.is_directory:
                target.mkdir(parents=True, exist_ok=False)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                source = archive.extractfile(info)
                if source is None:
                    raise VerificationError(f"unable to extract archive entry: {name}")
                with source, target.open("wb") as output:
                    shutil.copyfileobj(source, output)
                target.chmod(member.mode)


def extract_zip(
    archive_path: Path,
    destination: Path,
    members: dict[str, ArchiveMember],
) -> None:
    with zipfile.ZipFile(archive_path) as archive:
        for info in archive.infolist():
            name = info.filename.rstrip("/")
            member = members[name]
            target = destination.joinpath(*PurePosixPath(name).parts)
            if member.is_directory:
                target.mkdir(parents=True, exist_ok=False)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(info) as source, target.open("wb") as output:
                    shutil.copyfileobj(source, output)
                target.chmod(member.mode)


def verify_package(
    *,
    package_results: Path,
    publish_executable: Path,
    rid: str,
    expected_version: str,
    root: Path,
    timeout_seconds: float,
    smoke_runner: SmokeRunner = run_artifact_smoke,
) -> None:
    artifact = read_package_artifact(
        package_results=package_results,
        rid=rid,
        expected_version=expected_version,
        publish_executable=publish_executable,
    )
    verify_checksum(artifact)
    members = inspect_archive(artifact)
    smoke_runner(
        publish_executable,
        rid,
        expected_version,
        root / "publish smoke",
        timeout_seconds,
    )
    extracted_root = extract_archive(
        artifact,
        members,
        root / "archive extraction",
    )
    extracted_executable = extracted_root / (
        "mn" if rid == "linux-x64" else "mn.exe"
    )
    smoke_runner(
        extracted_executable,
        rid,
        expected_version,
        root / "archive smoke",
        timeout_seconds,
    )


def main(arguments: Sequence[str] | None = None) -> int:
    parsed = parse_arguments(sys.argv[1:] if arguments is None else arguments)
    try:
        validate_expected_version(parsed.expected_version)
        temporary_parent = resolve_temporary_root(parsed.temporary_root)
        if parsed.mode == "smoke":
            executable = resolve_existing_file(
                parsed.executable,
                "published executable",
            )
            execute_with_temporary_root(
                parent=temporary_parent,
                cleanup_on_failure=parsed.cleanup_on_failure,
                action=lambda root: run_artifact_smoke(
                    executable,
                    parsed.rid,
                    parsed.expected_version,
                    root,
                    parsed.timeout_seconds,
                ),
            )
        else:
            package_results = resolve_existing_file(
                parsed.package_results,
                "package results",
            )
            publish_executable = resolve_existing_file(
                parsed.publish_executable,
                "publish executable",
            )
            execute_with_temporary_root(
                parent=temporary_parent,
                cleanup_on_failure=parsed.cleanup_on_failure,
                action=lambda root: verify_package(
                    package_results=package_results,
                    publish_executable=publish_executable,
                    rid=parsed.rid,
                    expected_version=parsed.expected_version,
                    root=root,
                    timeout_seconds=parsed.timeout_seconds,
                ),
            )
    except (OSError, VerificationError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    print(
        f"Release artifact verification passed: {parsed.mode} "
        f"{parsed.rid} {parsed.expected_version}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
