#!/usr/bin/env python3
"""Validate release tags and manage draft GitHub Release assets."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Sequence


RELEASE_TAG_PATTERN = re.compile(
    r"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"
)
SUPPORTED_RIDS = ("linux-x64", "win-x64")
CHECKSUM_ASSET_NAME = "SHA256SUMS"


class ReleaseWorkflowError(RuntimeError):
    """Reports a release workflow contract failure."""


@dataclass(frozen=True)
class ReleaseState:
    tag: str
    is_draft: bool
    url: str
    asset_names: tuple[str, ...]


CommandRunner = Callable[[Sequence[str], Path | None], subprocess.CompletedProcess[str]]


def parse_arguments(arguments: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate release sources and safely manage a draft GitHub Release."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate = subparsers.add_parser(
        "validate",
        help="Validate a release tag, product version, event revision, and main ancestry.",
    )
    validate.add_argument("--tag", required=True)
    validate.add_argument("--event-sha", required=True)
    validate.add_argument("--main-ref", required=True)
    validate.add_argument("--repository-root", default=".")
    validate.add_argument("--github-output")

    prepare = subparsers.add_parser(
        "prepare",
        help="Create or reuse a draft Release and invalidate an old checksum asset.",
    )
    add_remote_options(prepare)
    prepare.add_argument("--github-output")

    upload = subparsers.add_parser(
        "upload",
        help="Upload one verified platform archive to an existing draft Release.",
    )
    add_remote_options(upload)
    upload.add_argument("--rid", required=True, choices=SUPPORTED_RIDS)
    upload.add_argument("--asset", required=True)

    finalize = subparsers.add_parser(
        "finalize",
        help="Download both archives, regenerate SHA256SUMS, and upload it.",
    )
    add_remote_options(finalize)
    finalize.add_argument("--download-directory", required=True)
    finalize.add_argument("--github-output")

    return parser.parse_args(arguments)


def add_remote_options(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--tag", required=True)
    parser.add_argument(
        "--repository",
        required=True,
        help="GitHub repository in owner/name form.",
    )


def normalize_release_tag(tag: str) -> str:
    match = RELEASE_TAG_PATTERN.fullmatch(tag)
    if match is None:
        raise ReleaseWorkflowError(
            "release tag must use v<major>.<minor>.<patch> with no leading zeros"
        )
    return ".".join(match.groups())


def archive_name(tag: str, rid: str) -> str:
    if rid not in SUPPORTED_RIDS:
        raise ReleaseWorkflowError(f"unsupported runtime identifier: {rid}")
    extension = ".tar.gz" if rid == "linux-x64" else ".zip"
    return f"mn-{tag}-{rid}{extension}"


def default_command_runner(
    command: Sequence[str], working_directory: Path | None
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=working_directory,
        check=False,
        capture_output=True,
        text=True,
    )


def run_checked(
    command: Sequence[str],
    *,
    working_directory: Path | None = None,
    runner: CommandRunner = default_command_runner,
) -> subprocess.CompletedProcess[str]:
    completed = runner(command, working_directory)
    if completed.returncode != 0:
        raise ReleaseWorkflowError(
            f"command failed with exit code {completed.returncode}: "
            f"{subprocess.list2cmdline(list(command))}\n"
            f"stdout:\n{completed.stdout}\n"
            f"stderr:\n{completed.stderr}"
        )
    return completed


def read_product_version(repository_root: Path) -> str:
    property_file = repository_root / "Directory.Build.props"
    if not property_file.is_file():
        raise ReleaseWorkflowError(f"product version file does not exist: {property_file}")
    try:
        root = ElementTree.parse(property_file).getroot()
    except ElementTree.ParseError as error:
        raise ReleaseWorkflowError(
            f"cannot parse product version file {property_file}: {error}"
        ) from error

    versions = {
        (element.text or "").strip()
        for element in root.iter()
        if element.tag.rsplit("}", 1)[-1] == "MemoriaNoteProductVersion"
        and (element.text or "").strip()
    }
    if len(versions) != 1:
        raise ReleaseWorkflowError(
            "Directory.Build.props must define exactly one MemoriaNoteProductVersion"
        )
    version = versions.pop()
    normalize_release_tag(f"v{version}")
    return version


def resolve_commit(
    revision: str,
    *,
    repository_root: Path,
    runner: CommandRunner = default_command_runner,
) -> str:
    completed = run_checked(
        ("git", "rev-parse", "--verify", f"{revision}^{{commit}}"),
        working_directory=repository_root,
        runner=runner,
    )
    commit = completed.stdout.strip().lower()
    if not re.fullmatch(r"[0-9a-f]{40,64}", commit):
        raise ReleaseWorkflowError(
            f"revision did not resolve to a full commit identifier: {revision}"
        )
    return commit


def validate_release_source(
    *,
    tag: str,
    event_sha: str,
    main_ref: str,
    repository_root: Path,
    runner: CommandRunner = default_command_runner,
) -> dict[str, str]:
    version = normalize_release_tag(tag)
    repository_root = repository_root.resolve()
    product_version = read_product_version(repository_root)
    if version != product_version:
        raise ReleaseWorkflowError(
            f"tag version {version} does not match MemoriaNoteProductVersion "
            f"{product_version}"
        )

    tag_commit = resolve_commit(
        f"refs/tags/{tag}", repository_root=repository_root, runner=runner
    )
    event_commit = resolve_commit(
        event_sha, repository_root=repository_root, runner=runner
    )
    if tag_commit != event_commit:
        raise ReleaseWorkflowError(
            f"tag commit {tag_commit} does not match event commit {event_commit}"
        )

    ancestry = runner(
        ("git", "merge-base", "--is-ancestor", tag_commit, main_ref),
        repository_root,
    )
    if ancestry.returncode == 1:
        raise ReleaseWorkflowError(
            f"release tag {tag} does not point to a commit contained in {main_ref}"
        )
    if ancestry.returncode != 0:
        raise ReleaseWorkflowError(
            f"unable to verify {main_ref} ancestry\n"
            f"stdout:\n{ancestry.stdout}\n"
            f"stderr:\n{ancestry.stderr}"
        )
    return {"tag": tag, "version": version, "commit": tag_commit}


def validate_repository_name(repository: str) -> None:
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ReleaseWorkflowError(
            "repository must use an owner/name value containing safe GitHub characters"
        )


def gh_command(repository: str, *arguments: str) -> tuple[str, ...]:
    validate_repository_name(repository)
    return ("gh", *arguments, "--repo", repository)


def parse_release_state(payload: str, expected_tag: str) -> ReleaseState:
    try:
        value = json.loads(payload)
        tag = value["tagName"]
        is_draft = value["isDraft"]
        url = value["url"]
        assets = value["assets"]
        asset_names = tuple(asset["name"] for asset in assets)
    except (KeyError, TypeError, json.JSONDecodeError) as error:
        raise ReleaseWorkflowError("GitHub CLI returned an invalid release payload") from error
    if tag != expected_tag:
        raise ReleaseWorkflowError(
            f"GitHub CLI returned release tag {tag!r}; expected {expected_tag!r}"
        )
    if not isinstance(is_draft, bool):
        raise ReleaseWorkflowError("GitHub CLI returned an invalid draft state")
    if not isinstance(url, str) or not url:
        raise ReleaseWorkflowError("GitHub CLI returned an invalid release URL")
    if any(not isinstance(name, str) or not name for name in asset_names):
        raise ReleaseWorkflowError("GitHub CLI returned an invalid release asset name")
    if len(set(asset_names)) != len(asset_names):
        raise ReleaseWorkflowError("draft Release contains duplicate asset names")
    return ReleaseState(tag, is_draft, url, asset_names)


def get_release_state(
    *,
    tag: str,
    repository: str,
    allow_missing: bool,
    runner: CommandRunner = default_command_runner,
) -> ReleaseState | None:
    command = gh_command(
        repository,
        "release",
        "view",
        tag,
        "--json",
        "tagName,isDraft,url,assets",
    )
    completed = runner(command, None)
    if completed.returncode == 0:
        return parse_release_state(completed.stdout, tag)
    if not allow_missing:
        raise ReleaseWorkflowError(
            f"unable to read GitHub Release for {tag}\n"
            f"stdout:\n{completed.stdout}\n"
            f"stderr:\n{completed.stderr}"
        )

    tag_check = runner(
        (
            "gh",
            "api",
            f"repos/{repository}/git/ref/tags/{tag}",
            "--silent",
        ),
        None,
    )
    if tag_check.returncode != 0:
        raise ReleaseWorkflowError(
            f"unable to confirm remote tag {tag} after release lookup failed\n"
            f"release stderr:\n{completed.stderr}\n"
            f"tag stderr:\n{tag_check.stderr}"
        )
    return None


def require_mutable_draft(state: ReleaseState) -> None:
    if not state.is_draft:
        raise ReleaseWorkflowError(
            f"release {state.tag} is already published; refusing to modify it"
        )


def release_body(tag: str, repository: str) -> str:
    checklist_url = (
        f"https://github.com/{repository}/blob/{tag}/"
        "reference/release/artifact-verification.md"
    )
    return (
        "This draft was created by the Memoria Note tag release workflow.\n\n"
        f"Do not publish it until the [release artifact verification checklist]"
        f"({checklist_url}) is complete.\n\n"
        "## Release evidence\n\n"
        "- [ ] Native Linux and Windows jobs succeeded.\n"
        "- [ ] `SHA256SUMS` was checked against both archives.\n"
        "- [ ] Runtime-free Linux and Windows checks succeeded.\n"
        "- [ ] Install, update, uninstall, and completion checks succeeded.\n"
        "- [ ] Target OS, shell versions, deviations, reviewer, and date are recorded."
    )


def delete_checksum_asset(
    *,
    state: ReleaseState,
    repository: str,
    runner: CommandRunner = default_command_runner,
) -> None:
    if CHECKSUM_ASSET_NAME not in state.asset_names:
        return
    run_checked(
        gh_command(
            repository,
            "release",
            "delete-asset",
            state.tag,
            CHECKSUM_ASSET_NAME,
            "--yes",
        ),
        runner=runner,
    )


def prepare_draft_release(
    *,
    tag: str,
    repository: str,
    runner: CommandRunner = default_command_runner,
) -> dict[str, object]:
    normalize_release_tag(tag)
    validate_repository_name(repository)
    state = get_release_state(
        tag=tag, repository=repository, allow_missing=True, runner=runner
    )
    reused = state is not None
    if state is None:
        run_checked(
            gh_command(
                repository,
                "release",
                "create",
                tag,
                "--draft",
                "--verify-tag",
                "--title",
                f"Memoria Note {tag}",
                "--generate-notes",
                "--notes",
                release_body(tag, repository),
            ),
            runner=runner,
        )
        state = get_release_state(
            tag=tag, repository=repository, allow_missing=False, runner=runner
        )
        if state is None:
            raise ReleaseWorkflowError("created draft Release could not be read")

    require_mutable_draft(state)
    delete_checksum_asset(state=state, repository=repository, runner=runner)
    return {"tag": tag, "url": state.url, "reused": reused}


def upload_archive(
    *,
    tag: str,
    repository: str,
    rid: str,
    asset: Path,
    runner: CommandRunner = default_command_runner,
) -> dict[str, str]:
    normalize_release_tag(tag)
    expected_name = archive_name(tag, rid)
    asset = asset.resolve()
    if not asset.is_file():
        raise ReleaseWorkflowError(f"release archive does not exist: {asset}")
    if asset.name != expected_name:
        raise ReleaseWorkflowError(
            f"release archive must be named {expected_name}; received {asset.name}"
        )
    state = get_release_state(
        tag=tag, repository=repository, allow_missing=False, runner=runner
    )
    if state is None:
        raise ReleaseWorkflowError(f"draft Release does not exist for {tag}")
    require_mutable_draft(state)
    run_checked(
        gh_command(
            repository,
            "release",
            "upload",
            tag,
            str(asset),
            "--clobber",
        ),
        runner=runner,
    )
    return {"tag": tag, "rid": rid, "asset": expected_name, "url": state.url}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def finalize_draft_release(
    *,
    tag: str,
    repository: str,
    download_directory: Path,
    runner: CommandRunner = default_command_runner,
) -> dict[str, str]:
    normalize_release_tag(tag)
    state = get_release_state(
        tag=tag, repository=repository, allow_missing=False, runner=runner
    )
    if state is None:
        raise ReleaseWorkflowError(f"draft Release does not exist for {tag}")
    require_mutable_draft(state)

    expected_names = tuple(archive_name(tag, rid) for rid in SUPPORTED_RIDS)
    missing = sorted(set(expected_names) - set(state.asset_names))
    allowed_names = set(expected_names) | {CHECKSUM_ASSET_NAME}
    unexpected = sorted(set(state.asset_names) - allowed_names)
    if missing:
        raise ReleaseWorkflowError(
            "draft Release is missing required archives: " + ", ".join(missing)
        )
    if unexpected:
        raise ReleaseWorkflowError(
            "draft Release contains unexpected assets: "
            + ", ".join(unexpected)
        )

    delete_checksum_asset(state=state, repository=repository, runner=runner)
    download_directory = download_directory.resolve()
    if not download_directory.is_dir():
        raise ReleaseWorkflowError(
            f"download directory does not exist: {download_directory}"
        )
    if any(download_directory.iterdir()):
        raise ReleaseWorkflowError(
            f"download directory must be empty: {download_directory}"
        )

    download_arguments: list[str] = ["release", "download", tag]
    for name in expected_names:
        download_arguments.extend(("--pattern", name))
    download_arguments.extend(("--dir", str(download_directory)))
    run_checked(gh_command(repository, *download_arguments), runner=runner)

    archives = [download_directory / name for name in expected_names]
    missing_downloads = [path.name for path in archives if not path.is_file()]
    if missing_downloads:
        raise ReleaseWorkflowError(
            "download did not produce required archives: "
            + ", ".join(missing_downloads)
        )
    unexpected_downloads = sorted(
        path.name
        for path in download_directory.iterdir()
        if path.name not in expected_names
    )
    if unexpected_downloads:
        raise ReleaseWorkflowError(
            "download produced unexpected entries: " + ", ".join(unexpected_downloads)
        )

    checksum_path = download_directory / CHECKSUM_ASSET_NAME
    checksum_text = "".join(
        f"{sha256(path)}  {path.name}\n" for path in sorted(archives)
    )
    checksum_path.write_text(checksum_text, encoding="utf-8", newline="\n")
    run_checked(
        gh_command(
            repository,
            "release",
            "upload",
            tag,
            str(checksum_path),
        ),
        runner=runner,
    )
    return {
        "tag": tag,
        "url": state.url,
        "checksum": str(checksum_path),
    }


def write_github_outputs(path: Path, values: dict[str, object]) -> None:
    with path.open("a", encoding="utf-8", newline="\n") as destination:
        for key, value in values.items():
            if not re.fullmatch(r"[a-z][a-z0-9_]*", key):
                raise ReleaseWorkflowError(f"invalid GitHub output name: {key}")
            rendered = str(value)
            if "\n" in rendered or "\r" in rendered:
                raise ReleaseWorkflowError(
                    f"GitHub output {key} must contain exactly one line"
                )
            destination.write(f"{key}={rendered}\n")


def main(arguments: Sequence[str] | None = None) -> int:
    parsed = parse_arguments(sys.argv[1:] if arguments is None else arguments)
    try:
        if parsed.command == "validate":
            result: dict[str, object] = validate_release_source(
                tag=parsed.tag,
                event_sha=parsed.event_sha,
                main_ref=parsed.main_ref,
                repository_root=Path(parsed.repository_root),
            )
            if parsed.github_output:
                write_github_outputs(Path(parsed.github_output), result)
        elif parsed.command == "prepare":
            result = prepare_draft_release(
                tag=parsed.tag,
                repository=parsed.repository,
            )
            if parsed.github_output:
                write_github_outputs(Path(parsed.github_output), result)
        elif parsed.command == "upload":
            result = upload_archive(
                tag=parsed.tag,
                repository=parsed.repository,
                rid=parsed.rid,
                asset=Path(parsed.asset),
            )
        else:
            result = finalize_draft_release(
                tag=parsed.tag,
                repository=parsed.repository,
                download_directory=Path(parsed.download_directory),
            )
            if parsed.github_output:
                write_github_outputs(Path(parsed.github_output), result)
        print(json.dumps(result, indent=2, sort_keys=True))
    except (OSError, ReleaseWorkflowError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
