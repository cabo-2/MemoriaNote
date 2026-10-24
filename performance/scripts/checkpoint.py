#!/usr/bin/env python3
"""Prepare, run, and report Memoria Note performance checkpoints."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import secrets
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import urllib.error
import urllib.request
import zipfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import BinaryIO, Callable, Mapping, Sequence


SCHEMA_VERSION = "1"
SUPPORTED_HOSTS = {"Linux": "linux", "Windows": "windows"}
SUPPORTED_ARCHITECTURES = {
    "amd64": "x64",
    "x86_64": "x64",
    "aarch64": "arm64",
    "arm64": "arm64",
}
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
ROADMAP_PATTERN = re.compile(r"^[A-Z][A-Z0-9-]*-[0-9]+[a-z]?$", re.ASCII)
IDENTIFIER_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$", re.ASCII)

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
PERFORMANCE_PROJECT = Path("tools/MemoriaNote.Performance")
DATASET_PROJECT = Path("tools/MemoriaNote.DatasetPreparation")
DEFAULT_CACHE_ROOT = Path("performance/.cache")
DEFAULT_RESULTS_ROOT = Path("performance/results")
DEFAULT_TEMPORARY_ROOT = Path("performance/.tmp/checkpoints")


class WorkflowError(RuntimeError):
    """Reports an expected operator or input failure."""


@dataclass(frozen=True)
class HostIdentity:
    os_name: str
    architecture: str


@dataclass(frozen=True)
class CommandResult:
    exit_code: int
    standard_output: str
    standard_error: str


@dataclass(frozen=True)
class RunSpec:
    name: str
    scenario_manifest: Path
    dataset_manifest: Path | None = None


DownloadFunction = Callable[[str, BinaryIO], None]
CommandFunction = Callable[[Sequence[str], Path], CommandResult]


RUN_SPECS = (
    RunSpec(
        "generic",
        Path("performance/scenarios/scenario-manifest-v1.extracted-archive.json"),
    ),
    RunSpec(
        "dotnet-docs-en",
        Path(
            "performance/scenarios/"
            "scenario-manifest-v1.dotnet-docs-en-pages-10000.extracted-archive.json"
        ),
        Path("performance/datasets/dotnet-docs-en/dataset-manifest.json"),
    ),
    RunSpec(
        "mdn-translated-content-ja",
        Path(
            "performance/scenarios/"
            "scenario-manifest-v1.mdn-translated-content-ja-pages-10000."
            "extracted-archive.json"
        ),
        Path("performance/datasets/mdn-translated-content-ja/dataset-manifest.json"),
    ),
)


def parse_arguments(arguments: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Prepare, run, and report published performance checkpoints."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    prepare = subparsers.add_parser(
        "prepare",
        help="Prepare both representative datasets and the selected artifact.",
    )
    add_artifact_options(prepare)
    prepare.add_argument(
        "--cache-root",
        default=str(DEFAULT_CACHE_ROOT),
        help="Dataset and artifact cache root (default: performance/.cache).",
    )

    run = subparsers.add_parser(
        "run",
        help="Run and validate generic, English, and Japanese measurements.",
    )
    add_artifact_options(run)
    run.add_argument("--roadmap-id", required=True, help="Roadmap item, such as ECLI-1010c.")
    run.add_argument(
        "--description",
        required=True,
        help="Short English checkpoint description.",
    )
    run.add_argument(
        "--environment-label",
        required=True,
        help="Stable, non-secret logical environment label.",
    )
    run.add_argument("--power-mode-note", help="Optional non-secret power mode note.")
    run.add_argument(
        "--cache-root",
        default=str(DEFAULT_CACHE_ROOT),
        help="Prepared input cache root (default: performance/.cache).",
    )
    run.add_argument(
        "--results-root",
        default=str(DEFAULT_RESULTS_ROOT),
        help="Local result root (default: performance/results).",
    )
    run.add_argument(
        "--temporary-root",
        default=str(DEFAULT_TEMPORARY_ROOT),
        help="Local temporary root (default: performance/.tmp/checkpoints).",
    )
    run.add_argument(
        "--keep-temporary",
        action="store_true",
        help="Retain the session temporary directory after the run.",
    )

    report = subparsers.add_parser(
        "report",
        help="Generate an English Markdown report from a checkpoint session.",
    )
    report.add_argument("--session", required=True, help="Path to session.json.")
    report.add_argument(
        "--output",
        help="New report path (default: docs/performance/<checkpoint>.md).",
    )
    return parser.parse_args(arguments)


def add_artifact_options(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--artifact-profile",
        required=True,
        help="Repository-relative artifact profile path.",
    )
    parser.add_argument(
        "--artifact-input",
        help="Optional explicit local archive or publish directory.",
    )


def resolve_from_repository(value: str | Path) -> Path:
    path = Path(value)
    return path.resolve() if path.is_absolute() else (REPOSITORY_ROOT / path).resolve()


def repository_relative(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPOSITORY_ROOT).as_posix()
    except ValueError as error:
        raise WorkflowError(f"Path must be inside the repository: {path.name}") from error


def display_local_path(path: Path) -> str:
    try:
        return repository_relative(path)
    except WorkflowError:
        return path.name


def read_json(path: Path, description: str) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        raise WorkflowError(f"{description} was not found: {path.name}") from error
    except (OSError, json.JSONDecodeError) as error:
        raise WorkflowError(f"{description} is not valid JSON: {path.name}") from error
    if not isinstance(value, dict):
        raise WorkflowError(f"{description} must contain a JSON object: {path.name}")
    return value


def require_exact_keys(
    value: Mapping[str, object],
    required: set[str],
    optional: set[str],
    description: str,
) -> None:
    missing = required - value.keys()
    unknown = value.keys() - required - optional
    if missing:
        raise WorkflowError(
            f"{description} is missing required fields: {', '.join(sorted(missing))}."
        )
    if unknown:
        raise WorkflowError(
            f"{description} contains unknown fields: {', '.join(sorted(unknown))}."
        )


def require_text(value: object, field: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise WorkflowError(f"{field} must be a non-empty string.")
    return value


def validate_relative_path(value: object, field: str) -> str:
    text = require_text(value, field)
    path = PurePosixPath(text)
    if path.is_absolute() or ".." in path.parts or text != path.as_posix():
        raise WorkflowError(f"{field} must be a normalized relative POSIX path.")
    return text


def load_artifact_profile(path: Path) -> dict:
    profile = read_json(path, "Artifact profile")
    required = {
        "schemaVersion",
        "artifactId",
        "productVersion",
        "sourceCommit",
        "hostOs",
        "rid",
        "configuration",
        "deploymentKind",
        "artifactInputKind",
        "executableRelativePath",
        "expectedVersionOutput",
    }
    require_exact_keys(profile, required, {"archive"}, "Artifact profile")
    if profile["schemaVersion"] != SCHEMA_VERSION:
        raise WorkflowError("Artifact profile schemaVersion is not supported.")
    artifact_id = require_text(profile["artifactId"], "artifactId")
    if not IDENTIFIER_PATTERN.fullmatch(artifact_id):
        raise WorkflowError("artifactId contains unsupported characters.")
    require_text(profile["productVersion"], "productVersion")
    source_commit = require_text(profile["sourceCommit"], "sourceCommit")
    if not COMMIT_PATTERN.fullmatch(source_commit):
        raise WorkflowError("sourceCommit must be a full lowercase Git commit SHA.")
    if profile["hostOs"] not in {"linux", "windows"}:
        raise WorkflowError("hostOs must be linux or windows.")
    rid = require_text(profile["rid"], "rid")
    expected_rid_prefix = "linux-" if profile["hostOs"] == "linux" else "win-"
    if not rid.startswith(expected_rid_prefix):
        raise WorkflowError("rid does not match hostOs.")
    if profile["configuration"] != "Release":
        raise WorkflowError("configuration must be Release.")
    if profile["deploymentKind"] not in {"self-contained", "native-aot"}:
        raise WorkflowError("deploymentKind is not supported.")
    if profile["artifactInputKind"] not in {
        "publish-directory",
        "extracted-archive",
    }:
        raise WorkflowError("artifactInputKind is not supported.")
    validate_relative_path(profile["executableRelativePath"], "executableRelativePath")
    require_text(profile["expectedVersionOutput"], "expectedVersionOutput")

    archive = profile.get("archive")
    if profile["artifactInputKind"] == "extracted-archive":
        if not isinstance(archive, dict):
            raise WorkflowError("extracted-archive profiles require archive metadata.")
        require_exact_keys(
            archive,
            {"format", "fileName", "url", "sha256", "rootDirectory"},
            set(),
            "Artifact archive",
        )
        if archive["format"] not in {"tar.gz", "zip"}:
            raise WorkflowError("archive.format must be tar.gz or zip.")
        file_name = validate_relative_path(archive["fileName"], "archive.fileName")
        if "/" in file_name:
            raise WorkflowError("archive.fileName must not contain a directory.")
        url = require_text(archive["url"], "archive.url")
        if not url.startswith("https://"):
            raise WorkflowError("archive.url must use HTTPS.")
        sha256 = require_text(archive["sha256"], "archive.sha256")
        if not SHA256_PATTERN.fullmatch(sha256):
            raise WorkflowError("archive.sha256 must be a lowercase SHA-256 value.")
        root = validate_relative_path(archive["rootDirectory"], "archive.rootDirectory")
        if "/" in root:
            raise WorkflowError("archive.rootDirectory must be one directory name.")
    elif archive is not None:
        raise WorkflowError("publish-directory profiles must not contain archive metadata.")
    return profile


def detect_host(
    system_name: str | None = None,
    machine_name: str | None = None,
) -> HostIdentity:
    raw_system = system_name or platform.system()
    raw_machine = (machine_name or platform.machine()).lower()
    os_name = SUPPORTED_HOSTS.get(raw_system)
    if os_name is None:
        raise WorkflowError(f"Unsupported host operating system: {raw_system or 'unknown'}.")
    architecture = SUPPORTED_ARCHITECTURES.get(raw_machine)
    if architecture is None:
        raise WorkflowError(f"Unsupported host architecture: {raw_machine or 'unknown'}.")
    return HostIdentity(os_name, architecture)


def validate_profile_host(profile: Mapping[str, object], host: HostIdentity) -> None:
    if profile["hostOs"] != host.os_name:
        raise WorkflowError(
            f"Artifact profile requires {profile['hostOs']}; host is {host.os_name}."
        )
    rid = str(profile["rid"])
    if not rid.endswith("-" + host.architecture):
        raise WorkflowError(
            f"Artifact profile RID '{rid}' does not match host architecture "
            f"'{host.architecture}'."
        )
    executable = str(profile["executableRelativePath"])
    if host.os_name == "windows" and not executable.lower().endswith(".exe"):
        raise WorkflowError("Windows artifact executable must end with .exe.")
    if host.os_name == "linux" and executable.lower().endswith(".exe"):
        raise WorkflowError("Linux artifact executable must not end with .exe.")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def default_download(url: str, destination: BinaryIO) -> None:
    request = urllib.request.Request(url, headers={"User-Agent": "MemoriaNote-checkpoint/1"})
    with urllib.request.urlopen(request, timeout=60) as response:
        shutil.copyfileobj(response, destination)


def ensure_downloaded_artifact(
    profile: Mapping[str, object],
    cache_root: Path,
    download: DownloadFunction = default_download,
) -> Path:
    archive = profile.get("archive")
    if not isinstance(archive, dict):
        raise WorkflowError("Artifact profile does not define a downloadable archive.")
    destination = cache_root / "artifacts" / str(profile["artifactId"]) / str(
        archive["fileName"]
    )
    expected_sha256 = str(archive["sha256"])
    if destination.exists():
        if not destination.is_file():
            raise WorkflowError("Artifact cache entry is not a file.")
        if sha256_file(destination) != expected_sha256:
            raise WorkflowError(
                "Cached artifact checksum mismatch; inspect or remove the cache entry explicitly."
            )
        print(f"Using cached artifact: {display_local_path(destination)}")
        return destination

    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w+b",
            prefix=destination.name + ".",
            suffix=".download",
            dir=destination.parent,
            delete=False,
        ) as stream:
            temporary_path = Path(stream.name)
            print(f"Downloading artifact: {archive['url']}")
            download(str(archive["url"]), stream)
            stream.flush()
            os.fsync(stream.fileno())
        if sha256_file(temporary_path) != expected_sha256:
            raise WorkflowError("Downloaded artifact checksum mismatch.")
        try:
            os.link(temporary_path, destination)
        except FileExistsError:
            if not destination.is_file() or sha256_file(destination) != expected_sha256:
                raise WorkflowError(
                    "Artifact cache entry changed during download and does not match."
                )
        temporary_path.unlink()
        temporary_path = None
    except WorkflowError:
        raise
    except (OSError, urllib.error.URLError) as error:
        raise WorkflowError(f"Artifact download failed: {error}") from error
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)
    print(f"Cached artifact: {display_local_path(destination)}")
    return destination


def run_command(arguments: Sequence[str], working_directory: Path) -> CommandResult:
    try:
        completed = subprocess.run(
            list(arguments),
            cwd=working_directory,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    except OSError as error:
        raise WorkflowError(f"Could not start '{arguments[0]}': {error}") from error
    return CommandResult(completed.returncode, completed.stdout, completed.stderr)


def echo_command_result(result: CommandResult) -> None:
    if result.standard_output:
        print(result.standard_output, end="" if result.standard_output.endswith("\n") else "\n")
    if result.standard_error:
        print(
            result.standard_error,
            end="" if result.standard_error.endswith("\n") else "\n",
            file=sys.stderr,
        )


def require_success(result: CommandResult, description: str) -> None:
    echo_command_result(result)
    if result.exit_code != 0:
        raise WorkflowError(f"{description} failed with exit code {result.exit_code}.")


def dataset_prepared_root(manifest: Mapping[str, object], cache_root: Path) -> Path:
    dataset_id = require_text(manifest.get("datasetId"), "datasetId")
    dataset_version = require_text(manifest.get("datasetVersion"), "datasetVersion")
    prepared = manifest.get("prepared")
    if not isinstance(prepared, dict):
        raise WorkflowError("Dataset manifest does not contain prepared metadata.")
    relative_path = validate_relative_path(prepared.get("relativePath"), "prepared.relativePath")
    return cache_root / "datasets" / dataset_id / dataset_version / relative_path


def dataset_tool_arguments(command: str, manifest_path: Path, cache_root: Path) -> list[str]:
    return [
        "dotnet",
        "run",
        "--configuration",
        "Release",
        "--project",
        str(DATASET_PROJECT),
        "--",
        command,
        "--manifest",
        str(manifest_path),
        "--cache-root",
        str(cache_root),
    ]


def prepare_dataset(
    manifest_path: Path,
    cache_root: Path,
    command: CommandFunction = run_command,
) -> None:
    manifest = read_json(manifest_path, "Dataset manifest")
    prepared_root = dataset_prepared_root(manifest, cache_root)
    if prepared_root.exists():
        print(f"Verifying cached dataset: {manifest['datasetId']}")
        result = command(
            dataset_tool_arguments("verify", manifest_path, cache_root), REPOSITORY_ROOT
        )
        require_success(result, f"Dataset verification for {manifest['datasetId']}")
        return

    print(f"Preparing dataset: {manifest['datasetId']}")
    result = command(
        dataset_tool_arguments("prepare", manifest_path, cache_root), REPOSITORY_ROOT
    )
    require_success(result, f"Dataset preparation for {manifest['datasetId']}")
    result = command(
        dataset_tool_arguments("verify", manifest_path, cache_root), REPOSITORY_ROOT
    )
    require_success(result, f"Dataset verification for {manifest['datasetId']}")


def verify_dataset(
    manifest_path: Path,
    cache_root: Path,
    command: CommandFunction = run_command,
) -> Path:
    manifest = read_json(manifest_path, "Dataset manifest")
    prepared_root = dataset_prepared_root(manifest, cache_root)
    if not prepared_root.is_dir():
        raise WorkflowError(
            f"Prepared dataset is missing for {manifest['datasetId']}; run prepare first."
        )
    result = command(
        dataset_tool_arguments("verify", manifest_path, cache_root), REPOSITORY_ROOT
    )
    require_success(result, f"Dataset verification for {manifest['datasetId']}")
    return prepared_root


def resolve_artifact_input(
    profile: Mapping[str, object],
    cache_root: Path,
    explicit_input: str | None,
) -> Path:
    if explicit_input:
        path = resolve_from_repository(explicit_input)
    elif profile["artifactInputKind"] == "extracted-archive":
        archive = profile["archive"]
        assert isinstance(archive, dict)
        path = cache_root / "artifacts" / str(profile["artifactId"]) / str(
            archive["fileName"]
        )
    else:
        raise WorkflowError("publish-directory profiles require --artifact-input.")

    if profile["artifactInputKind"] == "extracted-archive":
        if not path.is_file():
            raise WorkflowError("Artifact archive is missing; run prepare first.")
        archive = profile["archive"]
        assert isinstance(archive, dict)
        if sha256_file(path) != archive["sha256"]:
            raise WorkflowError("Artifact archive checksum mismatch.")
    elif not path.is_dir():
        raise WorkflowError("Artifact publish directory was not found.")
    return path


def safe_member_parts(name: str, expected_root: str) -> tuple[str, ...]:
    if "\\" in name or "\0" in name:
        raise WorkflowError(f"Archive contains an unsafe path: {name}")
    path = PurePosixPath(name)
    parts = tuple(part for part in path.parts if part not in {"", "."})
    if (
        path.is_absolute()
        or not parts
        or ".." in parts
        or any(":" in part for part in parts)
        or parts[0] != expected_root
    ):
        raise WorkflowError(f"Archive contains an unsafe path: {name}")
    return parts


def destination_for_member(destination: Path, parts: Sequence[str]) -> Path:
    target = destination.joinpath(*parts)
    try:
        target.resolve().relative_to(destination.resolve())
    except ValueError as error:
        raise WorkflowError("Archive member escapes the extraction root.") from error
    return target


def extract_tar_gz(archive_path: Path, destination: Path, expected_root: str) -> None:
    seen: set[tuple[str, ...]] = set()
    try:
        with tarfile.open(archive_path, mode="r:gz") as archive:
            for member in archive.getmembers():
                parts = safe_member_parts(member.name, expected_root)
                if parts in seen:
                    raise WorkflowError(f"Archive contains a duplicate path: {member.name}")
                seen.add(parts)
                target = destination_for_member(destination, parts)
                if member.isdir():
                    target.mkdir(parents=True, exist_ok=True)
                    continue
                if not member.isfile():
                    raise WorkflowError(
                        f"Archive contains an unsupported entry: {member.name}"
                    )
                target.parent.mkdir(parents=True, exist_ok=True)
                source = archive.extractfile(member)
                if source is None:
                    raise WorkflowError(f"Archive member could not be read: {member.name}")
                with source, target.open("xb") as output:
                    shutil.copyfileobj(source, output)
                target.chmod(member.mode & 0o777)
    except (tarfile.TarError, OSError) as error:
        raise WorkflowError(f"Could not extract tar archive: {error}") from error


def extract_zip(archive_path: Path, destination: Path, expected_root: str) -> None:
    seen: set[tuple[str, ...]] = set()
    try:
        with zipfile.ZipFile(archive_path) as archive:
            for member in archive.infolist():
                parts = safe_member_parts(member.filename, expected_root)
                if parts in seen:
                    raise WorkflowError(
                        f"Archive contains a duplicate path: {member.filename}"
                    )
                seen.add(parts)
                unix_mode = member.external_attr >> 16
                file_type = stat.S_IFMT(unix_mode)
                if file_type not in {0, stat.S_IFREG, stat.S_IFDIR}:
                    raise WorkflowError(
                        f"Archive contains an unsupported entry: {member.filename}"
                    )
                target = destination_for_member(destination, parts)
                if member.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                    continue
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(member) as source, target.open("xb") as output:
                    shutil.copyfileobj(source, output)
    except (zipfile.BadZipFile, OSError) as error:
        raise WorkflowError(f"Could not extract zip archive: {error}") from error


def prepare_executable(
    profile: Mapping[str, object],
    artifact_input: Path,
    extraction_root: Path,
) -> tuple[Path, Path | None]:
    if profile["artifactInputKind"] == "publish-directory":
        executable = artifact_input / str(profile["executableRelativePath"])
        archive_path = None
    else:
        archive = profile["archive"]
        assert isinstance(archive, dict)
        extraction_root.mkdir(parents=True, exist_ok=False)
        if archive["format"] == "tar.gz":
            extract_tar_gz(artifact_input, extraction_root, str(archive["rootDirectory"]))
        else:
            extract_zip(artifact_input, extraction_root, str(archive["rootDirectory"]))
        executable = (
            extraction_root
            / str(archive["rootDirectory"])
            / str(profile["executableRelativePath"])
        )
        archive_path = artifact_input
    if not executable.is_file():
        raise WorkflowError("Artifact executable was not found at the profiled location.")
    if profile["hostOs"] == "linux" and not os.access(executable, os.X_OK):
        raise WorkflowError("Linux artifact executable does not have an executable bit.")
    return executable, archive_path


def verify_product_version(
    profile: Mapping[str, object],
    executable: Path,
    command: CommandFunction = run_command,
) -> None:
    result = command([str(executable), "--version"], REPOSITORY_ROOT)
    if result.exit_code != 0:
        echo_command_result(result)
        raise WorkflowError("Artifact --version command failed.")
    if result.standard_error:
        raise WorkflowError("Artifact --version wrote to standard error.")
    if result.standard_output.strip() != profile["expectedVersionOutput"]:
        raise WorkflowError("Artifact --version output does not match the profile.")


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def utc_text(value: datetime) -> str:
    return value.isoformat(timespec="seconds").replace("+00:00", "Z")


def create_session_id(value: datetime) -> str:
    return value.strftime("%Y%m%dT%H%M%SZ") + "-" + secrets.token_hex(3)


def write_json_new_or_replace(path: Path, value: Mapping[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    os.replace(temporary, path)


def dotnet_sdk_version(command: CommandFunction = run_command) -> str:
    result = command(["dotnet", "--version"], REPOSITORY_ROOT)
    if result.exit_code != 0:
        echo_command_result(result)
        raise WorkflowError("dotnet --version failed.")
    return require_text(result.standard_output.strip(), ".NET SDK version")


def runner_base_arguments(action: str) -> list[str]:
    return [
        "dotnet",
        "run",
        "--configuration",
        "Release",
        "--no-build",
        "--project",
        str(PERFORMANCE_PROJECT),
        "--",
        action,
    ]


def runner_arguments(
    action: str,
    profile: Mapping[str, object],
    executable: Path,
    archive_path: Path | None,
    spec: RunSpec,
    environment_label: str,
    temporary_root: Path,
    output_path: Path | None,
    cache_root: Path,
    power_mode_note: str | None,
    keep_temporary: bool,
) -> list[str]:
    arguments = runner_base_arguments(action)
    arguments.extend(
        [
            "--scenario-manifest",
            repository_relative(REPOSITORY_ROOT / spec.scenario_manifest),
            "--executable",
            str(executable),
            "--artifact-id",
            str(profile["artifactId"]),
            "--product-version",
            str(profile["productVersion"]),
            "--source-commit",
            str(profile["sourceCommit"]),
            "--rid",
            str(profile["rid"]),
            "--configuration",
            str(profile["configuration"]),
            "--deployment-kind",
            str(profile["deploymentKind"]),
            "--artifact-input-kind",
            str(profile["artifactInputKind"]),
            "--environment-label",
            environment_label,
            "--temporary-root",
            str(temporary_root),
        ]
    )
    if archive_path is not None:
        arguments.extend(["--archive", str(archive_path)])
    if spec.dataset_manifest is not None:
        manifest_path = REPOSITORY_ROOT / spec.dataset_manifest
        manifest = read_json(manifest_path, "Dataset manifest")
        arguments.extend(
            [
                "--dataset-manifest",
                repository_relative(manifest_path),
                "--dataset-root",
                str(dataset_prepared_root(manifest, cache_root)),
            ]
        )
    if power_mode_note:
        arguments.extend(["--power-mode-note", power_mode_note])
    if output_path is not None:
        arguments.extend(["--output", str(output_path)])
    if keep_temporary:
        arguments.append("--keep-temporary")
    return arguments


def session_input_records(profile_path: Path) -> list[dict]:
    records = [
        {
            "kind": "artifact-profile",
            "path": repository_relative(profile_path),
            "sha256": sha256_file(profile_path),
        }
    ]
    paths = {spec.scenario_manifest for spec in RUN_SPECS}
    paths.update(
        spec.dataset_manifest for spec in RUN_SPECS if spec.dataset_manifest is not None
    )
    for relative_path in sorted(paths, key=lambda value: value.as_posix()):
        path = REPOSITORY_ROOT / relative_path
        records.append(
            {
                "kind": (
                    "scenario-manifest"
                    if "scenarios" in relative_path.parts
                    else "dataset-manifest"
                ),
                "path": relative_path.as_posix(),
                "sha256": sha256_file(path),
            }
        )
    return records


def validate_roadmap_id(value: str) -> None:
    if not ROADMAP_PATTERN.fullmatch(value):
        raise WorkflowError("roadmap-id is not in the expected form, such as ECLI-1010c.")


def validate_operator_text(value: str, field: str) -> None:
    if not value.strip() or "\n" in value or "\r" in value:
        raise WorkflowError(f"{field} must be a non-empty single-line value.")


def prepare_workflow(arguments: argparse.Namespace) -> None:
    host = detect_host()
    profile_path = resolve_from_repository(arguments.artifact_profile)
    repository_relative(profile_path)
    profile = load_artifact_profile(profile_path)
    validate_profile_host(profile, host)
    cache_root = resolve_from_repository(arguments.cache_root)
    repository_relative(cache_root)
    for spec in RUN_SPECS:
        if spec.dataset_manifest is not None:
            prepare_dataset(REPOSITORY_ROOT / spec.dataset_manifest, cache_root)
    if arguments.artifact_input:
        resolve_artifact_input(profile, cache_root, arguments.artifact_input)
        print("Verified explicit artifact input.")
    elif profile["artifactInputKind"] == "extracted-archive":
        ensure_downloaded_artifact(profile, cache_root)
    else:
        raise WorkflowError("publish-directory profiles require --artifact-input.")
    print("Performance inputs are prepared and verified.")


def create_session_record(
    session_id: str,
    started_at: datetime,
    arguments: argparse.Namespace,
    profile_path: Path,
    profile: Mapping[str, object],
    host: HostIdentity,
    sdk_version: str,
) -> dict:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "sessionId": session_id,
        "roadmapId": arguments.roadmap_id,
        "description": arguments.description,
        "environmentLabel": arguments.environment_label,
        "host": {"os": host.os_name, "architecture": host.architecture},
        "startedAtUtc": utc_text(started_at),
        "endedAtUtc": None,
        "status": "running",
        "dotnetSdkVersion": sdk_version,
        "runner": None,
        "artifact": {
            key: profile[key]
            for key in (
                "artifactId",
                "productVersion",
                "sourceCommit",
                "rid",
                "configuration",
                "deploymentKind",
                "artifactInputKind",
            )
        },
        "inputs": session_input_records(profile_path),
        "results": [],
        "failures": [],
    }


def run_workflow(arguments: argparse.Namespace) -> Path:
    validate_roadmap_id(arguments.roadmap_id)
    validate_operator_text(arguments.description, "description")
    validate_operator_text(arguments.environment_label, "environment-label")
    if arguments.power_mode_note:
        validate_operator_text(arguments.power_mode_note, "power-mode-note")
    host = detect_host()
    profile_path = resolve_from_repository(arguments.artifact_profile)
    repository_relative(profile_path)
    profile = load_artifact_profile(profile_path)
    validate_profile_host(profile, host)
    cache_root = resolve_from_repository(arguments.cache_root)
    results_root = resolve_from_repository(arguments.results_root)
    temporary_root = resolve_from_repository(arguments.temporary_root)
    repository_relative(cache_root)
    repository_relative(results_root)
    repository_relative(temporary_root)
    artifact_input = resolve_artifact_input(
        profile, cache_root, arguments.artifact_input
    )
    for spec in RUN_SPECS:
        if spec.dataset_manifest is not None:
            verify_dataset(REPOSITORY_ROOT / spec.dataset_manifest, cache_root)

    build = run_command(
        ["dotnet", "build", "MemoriaNote.sln", "--configuration", "Release"],
        REPOSITORY_ROOT,
    )
    require_success(build, "Release build")
    sdk_version = dotnet_sdk_version()

    started_at = utc_now()
    session_id = create_session_id(started_at)
    session_directory = (
        results_root / arguments.roadmap_id.lower() / session_id
    )
    session_directory.mkdir(parents=True, exist_ok=False)
    session_path = session_directory / "session.json"
    work_root = temporary_root / session_id
    work_root.mkdir(parents=True, exist_ok=False)
    session = create_session_record(
        session_id,
        started_at,
        arguments,
        profile_path,
        profile,
        host,
        sdk_version,
    )
    write_json_new_or_replace(session_path, session)
    stage = "artifact-preflight"
    try:
        executable, archive_path = prepare_executable(
            profile, artifact_input, work_root / "artifact"
        )
        verify_product_version(profile, executable)
        for spec in RUN_SPECS:
            stage = f"describe:{spec.name}"
            describe = run_command(
                runner_arguments(
                    "describe",
                    profile,
                    executable,
                    archive_path,
                    spec,
                    arguments.environment_label,
                    work_root / "runner",
                    None,
                    cache_root,
                    arguments.power_mode_note,
                    arguments.keep_temporary,
                ),
                REPOSITORY_ROOT,
            )
            require_success(describe, f"Describe for {spec.name}")

            result_path = session_directory / f"{spec.name}.json"
            stage = f"run:{spec.name}"
            measured = run_command(
                runner_arguments(
                    "run",
                    profile,
                    executable,
                    archive_path,
                    spec,
                    arguments.environment_label,
                    work_root / "runner",
                    result_path,
                    cache_root,
                    arguments.power_mode_note,
                    arguments.keep_temporary,
                ),
                REPOSITORY_ROOT,
            )
            echo_command_result(measured)
            if not result_path.is_file():
                raise WorkflowError(
                    f"Runner did not create the {spec.name} result "
                    f"(exit code {measured.exit_code})."
                )

            stage = f"validate:{spec.name}"
            validated = run_command(
                runner_base_arguments("validate-result")
                + ["--result", str(result_path)],
                REPOSITORY_ROOT,
            )
            require_success(validated, f"Result validation for {spec.name}")
            result_document = read_json(result_path, "Performance result")
            run_identity = result_document.get("run")
            if not isinstance(run_identity, dict):
                raise WorkflowError("Performance result does not contain run identity.")
            result_status = require_text(run_identity.get("status"), "run.status")
            runner_identity = {
                "version": require_text(
                    run_identity.get("runnerVersion"), "run.runnerVersion"
                ),
                "commit": require_text(
                    run_identity.get("runnerCommit"), "run.runnerCommit"
                ),
            }
            if session["runner"] is None:
                session["runner"] = runner_identity
            elif session["runner"] != runner_identity:
                raise WorkflowError("Performance results used different runner identities.")
            session["results"].append(
                {
                    "name": spec.name,
                    "path": repository_relative(result_path),
                    "sha256": sha256_file(result_path),
                    "status": result_status,
                    "validated": True,
                }
            )
            write_json_new_or_replace(session_path, session)
            if measured.exit_code != 0 or result_status != "valid":
                raise WorkflowError(f"Performance result for {spec.name} is invalid.")

        session["status"] = "valid"
        stage = "complete"
        return session_path
    except WorkflowError:
        session["status"] = "invalid"
        session["failures"].append(
            {"stage": stage, "message": "See the operator console output."}
        )
        raise
    finally:
        session["endedAtUtc"] = utc_text(utc_now())
        write_json_new_or_replace(session_path, session)
        if not arguments.keep_temporary:
            shutil.rmtree(work_root, ignore_errors=True)


def validate_session_record(session: Mapping[str, object]) -> None:
    required = {
        "schemaVersion",
        "sessionId",
        "roadmapId",
        "description",
        "environmentLabel",
        "host",
        "startedAtUtc",
        "endedAtUtc",
        "status",
        "dotnetSdkVersion",
        "runner",
        "artifact",
        "inputs",
        "results",
        "failures",
    }
    require_exact_keys(session, required, set(), "Checkpoint session")
    if session["schemaVersion"] != SCHEMA_VERSION:
        raise WorkflowError("Checkpoint session schemaVersion is not supported.")
    require_text(session["sessionId"], "sessionId")
    roadmap_id = require_text(session["roadmapId"], "roadmapId")
    validate_roadmap_id(roadmap_id)
    description = require_text(session["description"], "description")
    environment_label = require_text(session["environmentLabel"], "environmentLabel")
    validate_operator_text(description, "description")
    validate_operator_text(environment_label, "environmentLabel")
    require_text(session["startedAtUtc"], "startedAtUtc")
    if session["endedAtUtc"] is not None:
        require_text(session["endedAtUtc"], "endedAtUtc")
    if session["status"] not in {"running", "valid", "invalid"}:
        raise WorkflowError("Checkpoint session status is not supported.")
    require_text(session["dotnetSdkVersion"], "dotnetSdkVersion")
    runner = session["runner"]
    if runner is not None:
        if not isinstance(runner, dict):
            raise WorkflowError("Checkpoint session runner must be an object or null.")
        require_exact_keys(
            runner, {"version", "commit"}, set(), "Checkpoint session runner"
        )
        require_text(runner["version"], "runner.version")
        require_text(runner["commit"], "runner.commit")
    host = session["host"]
    if not isinstance(host, dict):
        raise WorkflowError("Checkpoint session host must be an object.")
    require_exact_keys(host, {"os", "architecture"}, set(), "Checkpoint session host")
    if host["os"] not in {"linux", "windows"}:
        raise WorkflowError("Checkpoint session host OS is not supported.")
    require_text(host["architecture"], "host.architecture")
    artifact = session["artifact"]
    if not isinstance(artifact, dict):
        raise WorkflowError("Checkpoint session artifact must be an object.")
    artifact_fields = {
        "artifactId",
        "productVersion",
        "sourceCommit",
        "rid",
        "configuration",
        "deploymentKind",
        "artifactInputKind",
    }
    require_exact_keys(artifact, artifact_fields, set(), "Checkpoint session artifact")
    for field in artifact_fields:
        require_text(artifact[field], f"artifact.{field}")
    if not COMMIT_PATTERN.fullmatch(str(artifact["sourceCommit"])):
        raise WorkflowError("artifact.sourceCommit must be a full lowercase Git commit SHA.")

    inputs = session["inputs"]
    results = session["results"]
    failures = session["failures"]
    for name, collection in (
        ("inputs", inputs),
        ("results", results),
        ("failures", failures),
    ):
        if not isinstance(collection, list):
            raise WorkflowError(f"Checkpoint session {name} must be an array.")
    assert isinstance(inputs, list)
    assert isinstance(results, list)
    assert isinstance(failures, list)
    for item in inputs:
        if not isinstance(item, dict):
            raise WorkflowError("Checkpoint input entry must be an object.")
        require_exact_keys(
            item, {"kind", "path", "sha256"}, set(), "Checkpoint input entry"
        )
        if item["kind"] not in {
            "artifact-profile",
            "scenario-manifest",
            "dataset-manifest",
        }:
            raise WorkflowError("Checkpoint input kind is not supported.")
        validate_relative_path(item["path"], "input.path")
        if not SHA256_PATTERN.fullmatch(require_text(item["sha256"], "input.sha256")):
            raise WorkflowError("input.sha256 must be a lowercase SHA-256 value.")
    result_names = []
    for item in results:
        if not isinstance(item, dict):
            raise WorkflowError("Checkpoint result entry must be an object.")
        require_exact_keys(
            item,
            {"name", "path", "sha256", "status", "validated"},
            set(),
            "Checkpoint result entry",
        )
        name = require_text(item["name"], "result.name")
        if not IDENTIFIER_PATTERN.fullmatch(name):
            raise WorkflowError("result.name contains unsupported characters.")
        result_names.append(name)
        validate_relative_path(item["path"], "result.path")
        if not SHA256_PATTERN.fullmatch(require_text(item["sha256"], "result.sha256")):
            raise WorkflowError("result.sha256 must be a lowercase SHA-256 value.")
        if item["status"] not in {"valid", "invalid"}:
            raise WorkflowError("Checkpoint result status is not supported.")
        if not isinstance(item["validated"], bool):
            raise WorkflowError("Checkpoint result validated must be a boolean.")
    if len(result_names) != len(set(result_names)):
        raise WorkflowError("Checkpoint result names must be unique.")
    if session["status"] == "valid":
        if runner is None:
            raise WorkflowError("Valid checkpoint session does not identify the runner.")
        expected_names = [spec.name for spec in RUN_SPECS]
        if result_names != expected_names:
            raise WorkflowError("Valid checkpoint session does not contain every result.")
        if any(item["status"] != "valid" or not item["validated"] for item in results):
            raise WorkflowError("Valid checkpoint session contains an invalid result entry.")
    for item in failures:
        if not isinstance(item, dict):
            raise WorkflowError("Checkpoint failure entry must be an object.")
        require_exact_keys(
            item, {"stage", "message"}, set(), "Checkpoint failure entry"
        )
        require_text(item["stage"], "failure.stage")
        require_text(item["message"], "failure.message")


def validate_report_inputs(session: Mapping[str, object], results: Sequence[dict]) -> None:
    if not results:
        raise WorkflowError("Checkpoint session does not contain performance results.")
    session_results = session["results"]
    assert isinstance(session_results, list)
    if len(results) != len(session_results):
        raise WorkflowError("Checkpoint session and performance result counts differ.")
    artifact = session["artifact"]
    assert isinstance(artifact, dict)
    expected_target = None
    expected_environment = None
    expected_runner = None
    for result in results:
        target = result.get("target")
        environment = result.get("environment")
        run = result.get("run")
        if not all(isinstance(value, dict) for value in (target, environment, run)):
            raise WorkflowError("Performance result identity is incomplete.")
        assert isinstance(target, dict)
        assert isinstance(environment, dict)
        assert isinstance(run, dict)
        for key in (
            "artifactId",
            "productVersion",
            "sourceCommit",
            "rid",
            "configuration",
            "deploymentKind",
            "artifactInputKind",
        ):
            if target.get(key) != artifact.get(key):
                raise WorkflowError(f"Performance result target mismatch: {key}.")
        if environment.get("environmentLabel") != session["environmentLabel"]:
            raise WorkflowError("Performance result environment label mismatch.")
        target_identity = tuple(sorted(target.items()))
        environment_identity = json.dumps(environment, sort_keys=True)
        runner_identity = (run.get("runnerVersion"), run.get("runnerCommit"))
        session_runner = session.get("runner")
        if isinstance(session_runner, dict) and runner_identity != (
            session_runner.get("version"),
            session_runner.get("commit"),
        ):
            raise WorkflowError("Performance result runner identity mismatch.")
        if expected_target is None:
            expected_target = target_identity
            expected_environment = environment_identity
            expected_runner = runner_identity
        elif (
            target_identity != expected_target
            or environment_identity != expected_environment
            or runner_identity != expected_runner
        ):
            raise WorkflowError("Performance results do not share one identity.")


def markdown(value: object) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def code(value: object) -> str:
    return "`" + markdown(value).replace("`", "\\`") + "`"


def format_bytes(value: object) -> str:
    if not isinstance(value, int):
        return "N/A"
    return f"{value} bytes ({value / (1024 ** 3):.2f} GiB)"


def format_milliseconds(value: object) -> str:
    if not isinstance(value, int):
        return "N/A"
    return f"{value / 1_000_000:.3f}"


def render_report(session: Mapping[str, object], results: Sequence[dict]) -> str:
    validate_session_record(session)
    validate_report_inputs(session, results)
    artifact = session["artifact"]
    assert isinstance(artifact, dict)
    first = results[0]
    environment = first["environment"]
    run = first["run"]
    target = first["target"]
    assert isinstance(environment, dict)
    assert isinstance(run, dict)
    assert isinstance(target, dict)

    lines = [
        f"# {markdown(session['description'])}",
        "",
        f"- Roadmap ID: {code(session['roadmapId'])}",
        f"- Status: {code(session['status'])}",
        f"- Session ID: {code(session['sessionId'])}",
        f"- Started at: {code(session['startedAtUtc'])}",
        f"- Finished at: {code(session['endedAtUtc'] or 'N/A')}",
        f"- Raw results: {code('performance/results/' + str(session['roadmapId']).lower() + '/' + str(session['sessionId']))}",
        "",
        "## Reference environment",
        "",
        "| Item | Value |",
        "| --- | --- |",
        f"| Environment label | {markdown(session['environmentLabel'])} |",
        f"| Host OS / architecture | {markdown(session['host']['os'])} / {markdown(session['host']['architecture'])} |",
        f"| OS description | {markdown(environment.get('osDescription', 'N/A'))} |",
        f"| Process architecture | {markdown(environment.get('processArchitecture', 'N/A'))} |",
        f"| CPU / logical processors | {markdown(environment.get('cpuModel', 'N/A'))} / {markdown(environment.get('logicalProcessorCount', 'N/A'))} |",
        f"| Installed memory | {format_bytes(environment.get('installedMemoryBytes'))} |",
        f"| Filesystem | {markdown(environment.get('fileSystem', 'N/A'))} |",
        f"| .NET SDK | {markdown(session['dotnetSdkVersion'])} |",
    ]
    if environment.get("powerModeNote"):
        lines.append(f"| Power mode | {markdown(environment['powerModeNote'])} |")

    lines.extend(
        [
            "",
            "## Artifact and runner",
            "",
            "| Item | Value |",
            "| --- | --- |",
            f"| Artifact ID | {markdown(target['artifactId'])} |",
            f"| Product version | {markdown(target['productVersion'])} |",
            f"| Source commit | {code(target['sourceCommit'])} |",
            f"| RID / deployment | {markdown(target['rid'])} / {markdown(target['deploymentKind'])} |",
            f"| Artifact input | {markdown(target['artifactInputKind'])} |",
            f"| Executable SHA-256 | {code(target['executableSha256'])} |",
            f"| Executable size | {markdown(target['executableSizeBytes'])} bytes |",
            f"| Archive SHA-256 | {code(target.get('archiveSha256', 'N/A'))} |",
            f"| Runner version | {markdown(run['runnerVersion'])} |",
            f"| Runner commit | {code(run['runnerCommit'])} |",
            "",
            "## Inputs",
            "",
            "| Kind | Path | SHA-256 |",
            "| --- | --- | --- |",
        ]
    )
    inputs = session["inputs"]
    assert isinstance(inputs, list)
    for item in inputs:
        lines.append(
            f"| {markdown(item['kind'])} | {code(item['path'])} | {code(item['sha256'])} |"
        )

    datasets = [result.get("dataset") for result in results if result.get("dataset")]
    if datasets:
        lines.extend(
            [
                "",
                "## Datasets",
                "",
                "| Dataset | Version | Source revision | Prepared SHA-256 |",
                "| --- | --- | --- | --- |",
            ]
        )
        for dataset in datasets:
            assert isinstance(dataset, dict)
            lines.append(
                f"| {markdown(dataset['datasetId'])} | {markdown(dataset['datasetVersion'])} | "
                f"{code(dataset['sourceRevision'])} | {code(dataset['preparedSha256'])} |"
            )

    lines.extend(
        [
            "",
            "## Validated results",
            "",
            "Times are converted from nanoseconds to milliseconds and rounded to three decimal places.",
            "",
            "| Result | Scenario | Status | n | p50 ms | p95 ms | min ms | max ms | mean ms |",
            "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |",
        ]
    )
    session_results = session["results"]
    assert isinstance(session_results, list)
    paths_by_name = {entry["name"]: entry["path"] for entry in session_results}
    for result, entry in zip(results, session_results, strict=True):
        for scenario in result.get("scenarioResults", []):
            summary = scenario.get("summary") or {}
            lines.append(
                f"| {code(paths_by_name[entry['name']])} | {markdown(scenario['scenarioId'])} | "
                f"{markdown(scenario['status'])} | {markdown(summary.get('sampleCount', 'N/A'))} | "
                f"{format_milliseconds(summary.get('p50Nanoseconds'))} | "
                f"{format_milliseconds(summary.get('p95Nanoseconds'))} | "
                f"{format_milliseconds(summary.get('minNanoseconds'))} | "
                f"{format_milliseconds(summary.get('maxNanoseconds'))} | "
                f"{format_milliseconds(summary.get('arithmeticMeanNanoseconds'))} |"
            )

    failures = session["failures"]
    assert isinstance(failures, list)
    if failures:
        lines.extend(["", "## Failures and deviations", ""])
        for failure in failures:
            lines.append(
                f"- {markdown(failure.get('stage', 'unknown'))}: "
                f"{markdown(failure.get('message', 'No details recorded.'))}"
            )
    return "\n".join(lines) + "\n"


def load_results_for_report(session: Mapping[str, object]) -> list[dict]:
    results = []
    session_results = session["results"]
    assert isinstance(session_results, list)
    for entry in session_results:
        if not isinstance(entry, dict):
            raise WorkflowError("Checkpoint result entry must be an object.")
        require_exact_keys(
            entry,
            {"name", "path", "sha256", "status", "validated"},
            set(),
            "Checkpoint result entry",
        )
        relative_path = validate_relative_path(entry["path"], "result.path")
        result_path = resolve_from_repository(relative_path)
        repository_relative(result_path)
        if not result_path.is_file():
            raise WorkflowError(f"Performance result was not found: {Path(relative_path).name}")
        if sha256_file(result_path) != entry["sha256"]:
            raise WorkflowError("Performance result checksum does not match the session.")
        validation = run_command(
            runner_base_arguments("validate-result") + ["--result", str(result_path)],
            REPOSITORY_ROOT,
        )
        require_success(validation, f"Result validation for {entry['name']}")
        results.append(read_json(result_path, "Performance result"))
    return results


def default_report_path(session: Mapping[str, object]) -> Path:
    date = str(session["startedAtUtc"])[:10]
    artifact = session["artifact"]
    assert isinstance(artifact, dict)
    name = (
        f"{date}-{str(session['roadmapId']).lower()}-"
        f"{str(artifact['artifactId']).lower()}-performance.md"
    )
    return REPOSITORY_ROOT / "docs" / "performance" / name


def report_workflow(arguments: argparse.Namespace) -> Path:
    session_path = resolve_from_repository(arguments.session)
    session = read_json(session_path, "Checkpoint session")
    validate_session_record(session)
    build = run_command(
        [
            "dotnet",
            "build",
            str(PERFORMANCE_PROJECT),
            "--configuration",
            "Release",
        ],
        REPOSITORY_ROOT,
    )
    require_success(build, "Performance runner build")
    results = load_results_for_report(session)
    report = render_report(session, results)
    output_path = (
        resolve_from_repository(arguments.output)
        if arguments.output
        else default_report_path(session)
    )
    if output_path.exists():
        raise WorkflowError("Report output already exists; choose a new path.")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(report, encoding="utf-8", newline="\n")
    return output_path


def main(arguments: Sequence[str] | None = None) -> int:
    try:
        parsed = parse_arguments(arguments if arguments is not None else sys.argv[1:])
        if parsed.command == "prepare":
            prepare_workflow(parsed)
        elif parsed.command == "run":
            session_path = run_workflow(parsed)
            print(f"Checkpoint session: {repository_relative(session_path)}")
        elif parsed.command == "report":
            report_path = report_workflow(parsed)
            try:
                display_path = repository_relative(report_path)
            except WorkflowError:
                display_path = str(report_path)
            print(f"Checkpoint report: {display_path}")
        else:
            raise WorkflowError("Unsupported workflow command.")
        return 0
    except WorkflowError as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Error: Workflow cancelled by the operator.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
