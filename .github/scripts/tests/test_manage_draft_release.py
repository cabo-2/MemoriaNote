from __future__ import annotations

import hashlib
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "manage-draft-release.py"
SPEC = importlib.util.spec_from_file_location("manage_draft_release", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load release script: {SCRIPT_PATH}")
RELEASE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = RELEASE
SPEC.loader.exec_module(RELEASE)


def completed(command, returncode=0, stdout="", stderr=""):
    return subprocess.CompletedProcess(command, returncode, stdout, stderr)


def release_payload(
    *,
    tag="v0.2.0",
    draft=True,
    assets=(),
):
    return json.dumps(
        {
            "tagName": tag,
            "isDraft": draft,
            "url": f"https://github.com/example/memoria/releases/tag/{tag}",
            "assets": [{"name": name} for name in assets],
        }
    )


class TagContractTests(unittest.TestCase):
    def test_normalize_release_tag_accepts_strict_versions(self) -> None:
        self.assertEqual(RELEASE.normalize_release_tag("v0.2.0"), "0.2.0")
        self.assertEqual(RELEASE.normalize_release_tag("v12.34.56"), "12.34.56")

    def test_normalize_release_tag_rejects_ambiguous_versions(self) -> None:
        for tag in (
            "0.2.0",
            "V0.2.0",
            "v0.2",
            "v0.2.0-rc.1",
            "v00.2.0",
            "v0.02.0",
            "v0.2.00",
        ):
            with self.subTest(tag=tag):
                with self.assertRaises(RELEASE.ReleaseWorkflowError):
                    RELEASE.normalize_release_tag(tag)

    def test_archive_names_match_packaging_contract(self) -> None:
        self.assertEqual(
            RELEASE.archive_name("v0.2.0", "linux-x64"),
            "mn-v0.2.0-linux-x64.tar.gz",
        )
        self.assertEqual(
            RELEASE.archive_name("v0.2.0", "win-x64"),
            "mn-v0.2.0-win-x64.zip",
        )


class SourceValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.repository = Path(self.temporary_directory.name)
        self.run_git("init", "--initial-branch=main")
        self.run_git("config", "user.name", "Release Test")
        self.run_git("config", "user.email", "release@example.invalid")
        (self.repository / "Directory.Build.props").write_text(
            "<Project>\n"
            "  <PropertyGroup>\n"
            "    <MemoriaNoteProductVersion>0.2.0</MemoriaNoteProductVersion>\n"
            "  </PropertyGroup>\n"
            "</Project>\n",
            encoding="utf-8",
        )
        self.run_git("add", "Directory.Build.props")
        self.run_git("commit", "-m", "release source")
        self.commit = self.run_git("rev-parse", "HEAD").stdout.strip()
        self.run_git("update-ref", "refs/remotes/origin/main", self.commit)

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def run_git(self, *arguments):
        return subprocess.run(
            ("git", *arguments),
            cwd=self.repository,
            check=True,
            capture_output=True,
            text=True,
        )

    def validate(self):
        return RELEASE.validate_release_source(
            tag="v0.2.0",
            event_sha=self.commit,
            main_ref="refs/remotes/origin/main",
            repository_root=self.repository,
        )

    def test_lightweight_tag_resolves_to_release_commit(self) -> None:
        self.run_git("tag", "v0.2.0", self.commit)

        result = self.validate()

        self.assertEqual(
            result,
            {"tag": "v0.2.0", "version": "0.2.0", "commit": self.commit},
        )

    def test_annotated_tag_resolves_to_release_commit(self) -> None:
        self.run_git("tag", "--annotate", "v0.2.0", "--message", "release")

        result = self.validate()

        self.assertEqual(result["commit"], self.commit)

    def test_tag_version_must_match_checked_in_product_version(self) -> None:
        self.run_git("tag", "v0.3.0", self.commit)

        with self.assertRaisesRegex(
            RELEASE.ReleaseWorkflowError,
            "does not match MemoriaNoteProductVersion",
        ):
            RELEASE.validate_release_source(
                tag="v0.3.0",
                event_sha=self.commit,
                main_ref="refs/remotes/origin/main",
                repository_root=self.repository,
            )

    def test_release_commit_must_be_contained_in_main(self) -> None:
        self.run_git("checkout", "--orphan", "release-candidate")
        self.run_git("rm", "--cached", "Directory.Build.props")
        self.run_git("add", "Directory.Build.props")
        self.run_git("commit", "-m", "unmerged release")
        candidate = self.run_git("rev-parse", "HEAD").stdout.strip()
        self.run_git("tag", "v0.2.0", candidate)

        with self.assertRaisesRegex(
            RELEASE.ReleaseWorkflowError,
            "does not point to a commit contained",
        ):
            RELEASE.validate_release_source(
                tag="v0.2.0",
                event_sha=candidate,
                main_ref="refs/remotes/origin/main",
                repository_root=self.repository,
            )


class DraftPreparationTests(unittest.TestCase):
    def test_prepare_creates_draft_only_for_existing_remote_tag(self) -> None:
        commands = []
        views = 0

        def runner(command, working_directory):
            nonlocal views
            commands.append(tuple(command))
            if command[1:3] == ("release", "view"):
                views += 1
                if views == 1:
                    return completed(command, 1, stderr="release not found")
                return completed(command, stdout=release_payload())
            if command[1:3] == ("api", "repos/example/memoria/git/ref/tags/v0.2.0"):
                return completed(command)
            if command[1:3] == ("release", "create"):
                return completed(command)
            self.fail(f"unexpected command: {command}")

        result = RELEASE.prepare_draft_release(
            tag="v0.2.0",
            repository="example/memoria",
            runner=runner,
        )

        create = next(command for command in commands if command[1:3] == ("release", "create"))
        self.assertIn("--draft", create)
        self.assertIn("--verify-tag", create)
        self.assertIn("--generate-notes", create)
        self.assertFalse(result["reused"])

    def test_prepare_reuses_draft_preserves_body_and_removes_old_checksum(self) -> None:
        commands = []

        def runner(command, working_directory):
            commands.append(tuple(command))
            if command[1:3] == ("release", "view"):
                return completed(
                    command,
                    stdout=release_payload(assets=("SHA256SUMS",)),
                )
            if command[1:3] == ("release", "delete-asset"):
                return completed(command)
            self.fail(f"unexpected command: {command}")

        result = RELEASE.prepare_draft_release(
            tag="v0.2.0",
            repository="example/memoria",
            runner=runner,
        )

        self.assertTrue(result["reused"])
        self.assertTrue(
            any(command[1:3] == ("release", "delete-asset") for command in commands)
        )
        self.assertFalse(
            any(command[1:3] == ("release", "create") for command in commands)
        )

    def test_prepare_refuses_to_modify_published_release(self) -> None:
        def runner(command, working_directory):
            return completed(command, stdout=release_payload(draft=False))

        with self.assertRaisesRegex(
            RELEASE.ReleaseWorkflowError,
            "already published",
        ):
            RELEASE.prepare_draft_release(
                tag="v0.2.0",
                repository="example/memoria",
                runner=runner,
            )


class AssetWorkflowTests(unittest.TestCase):
    def test_upload_accepts_only_expected_verified_archive_name(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "mn-v0.2.0-linux-x64.tar.gz"
            archive.write_bytes(b"linux archive")
            commands = []

            def runner(command, working_directory):
                commands.append(tuple(command))
                if command[1:3] == ("release", "view"):
                    return completed(command, stdout=release_payload())
                if command[1:3] == ("release", "upload"):
                    return completed(command)
                self.fail(f"unexpected command: {command}")

            result = RELEASE.upload_archive(
                tag="v0.2.0",
                repository="example/memoria",
                rid="linux-x64",
                asset=archive,
                runner=runner,
            )

            upload = next(
                command for command in commands if command[1:3] == ("release", "upload")
            )
            self.assertIn("--clobber", upload)
            self.assertEqual(result["asset"], archive.name)

    def test_finalize_downloads_both_archives_and_regenerates_checksum(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            download = Path(directory)
            linux_name = "mn-v0.2.0-linux-x64.tar.gz"
            windows_name = "mn-v0.2.0-win-x64.zip"
            linux_bytes = b"linux archive"
            windows_bytes = b"windows archive"
            commands = []

            def runner(command, working_directory):
                commands.append(tuple(command))
                if command[1:3] == ("release", "view"):
                    return completed(
                        command,
                        stdout=release_payload(
                            assets=(linux_name, windows_name, "SHA256SUMS")
                        ),
                    )
                if command[1:3] == ("release", "delete-asset"):
                    return completed(command)
                if command[1:3] == ("release", "download"):
                    (download / linux_name).write_bytes(linux_bytes)
                    (download / windows_name).write_bytes(windows_bytes)
                    return completed(command)
                if command[1:3] == ("release", "upload"):
                    return completed(command)
                self.fail(f"unexpected command: {command}")

            result = RELEASE.finalize_draft_release(
                tag="v0.2.0",
                repository="example/memoria",
                download_directory=download,
                runner=runner,
            )

            checksum = download / "SHA256SUMS"
            expected = (
                f"{hashlib.sha256(linux_bytes).hexdigest()}  {linux_name}\n"
                f"{hashlib.sha256(windows_bytes).hexdigest()}  {windows_name}\n"
            )
            self.assertEqual(checksum.read_text(encoding="utf-8"), expected)
            self.assertEqual(result["checksum"], str(checksum))
            self.assertTrue(
                any(command[1:3] == ("release", "download") for command in commands)
            )

    def test_finalize_refuses_partial_draft(self) -> None:
        def runner(command, working_directory):
            return completed(
                command,
                stdout=release_payload(assets=("mn-v0.2.0-linux-x64.tar.gz",)),
            )

        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(
                RELEASE.ReleaseWorkflowError,
                "missing required archives",
            ):
                RELEASE.finalize_draft_release(
                    tag="v0.2.0",
                    repository="example/memoria",
                    download_directory=Path(directory),
                    runner=runner,
                )

    def test_finalize_refuses_unexpected_release_asset(self) -> None:
        expected_assets = (
            "mn-v0.2.0-linux-x64.tar.gz",
            "mn-v0.2.0-win-x64.zip",
            "unexpected.txt",
        )

        def runner(command, working_directory):
            return completed(
                command,
                stdout=release_payload(assets=expected_assets),
            )

        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(
                RELEASE.ReleaseWorkflowError,
                "contains unexpected assets",
            ):
                RELEASE.finalize_draft_release(
                    tag="v0.2.0",
                    repository="example/memoria",
                    download_directory=Path(directory),
                    runner=runner,
                )


class GitHubOutputTests(unittest.TestCase):
    def test_outputs_are_appended_as_single_lines(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "github-output"
            output.write_text("existing=value\n", encoding="utf-8")

            RELEASE.write_github_outputs(
                output,
                {"tag": "v0.2.0", "version": "0.2.0"},
            )

            self.assertEqual(
                output.read_text(encoding="utf-8"),
                "existing=value\ntag=v0.2.0\nversion=0.2.0\n",
            )


if __name__ == "__main__":
    unittest.main()
