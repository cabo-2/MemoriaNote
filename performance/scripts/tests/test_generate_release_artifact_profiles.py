from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
SCRIPT_SOURCE = (
    REPOSITORY_ROOT
    / "performance"
    / "scripts"
    / "generate-release-artifact-profiles.sh"
)
TAG = "v1.2.3"
VERSION = "1.2.3"
SOURCE_COMMIT = "1" * 40
REPOSITORY = "example/MemoriaNote"
LINUX_ARCHIVE = f"mn-{TAG}-linux-x64.tar.gz"
WINDOWS_ARCHIVE = f"mn-{TAG}-win-x64.zip"


def find_bash() -> str | None:
    candidates: list[Path] = []
    if os.name == "nt":
        git = shutil.which("git")
        if git:
            candidates.append(Path(git).resolve().parent.parent / "bin" / "bash.exe")
        for variable in ("ProgramFiles", "ProgramFiles(x86)"):
            root = os.environ.get(variable)
            if root:
                candidates.append(Path(root) / "Git" / "bin" / "bash.exe")

    discovered = shutil.which("bash")
    if discovered:
        candidates.append(Path(discovered))

    checked: set[str] = set()
    for candidate in candidates:
        key = str(candidate).casefold()
        if key in checked or not candidate.is_file():
            continue
        checked.add(key)
        try:
            completed = subprocess.run(
                [str(candidate), "--version"],
                check=False,
                capture_output=True,
                text=True,
                timeout=10,
            )
        except (OSError, subprocess.TimeoutExpired):
            continue
        if completed.returncode == 0 and "GNU bash" in completed.stdout:
            return str(candidate)
    return None


BASH_COMMAND = find_bash()


@unittest.skipUnless(BASH_COMMAND, "GNU Bash is required")
class GenerateReleaseArtifactProfilesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.script = (
            self.root
            / "performance"
            / "scripts"
            / "generate-release-artifact-profiles.sh"
        )
        self.script.parent.mkdir(parents=True)
        shutil.copy2(SCRIPT_SOURCE, self.script)
        self.script.chmod(0o755)

        self.fixture = self.root / "fixture"
        self.assets = self.fixture / "assets"
        self.assets.mkdir(parents=True)
        (self.assets / LINUX_ARCHIVE).write_bytes(b"linux archive fixture")
        (self.assets / WINDOWS_ARCHIVE).write_bytes(b"windows archive fixture")
        self.write_checksums()
        self.write_release()
        (self.fixture / "Directory.Build.props").write_text(
            "<Project><PropertyGroup>"
            f"<MemoriaNoteProductVersion>{VERSION}</MemoriaNoteProductVersion>"
            "</PropertyGroup></Project>\n",
            encoding="utf-8",
        )

        self.fake_gh = self.fixture / "fake-gh"
        self.fake_gh.write_text(
            """#!/usr/bin/env bash
set -euo pipefail

case "$1 $2" in
    "repo view")
        printf '%s\\n' "$FAKE_GH_REPOSITORY"
        ;;
    "release view")
        cat "$FAKE_GH_FIXTURE/release.json"
        ;;
    "release download")
        shift 2
        destination=""
        patterns=()
        while [[ $# -gt 0 ]]; do
            case "$1" in
                --repo)
                    shift 2
                    ;;
                --dir)
                    destination="$2"
                    shift 2
                    ;;
                --pattern)
                    patterns+=("$2")
                    shift 2
                    ;;
                *)
                    shift
                    ;;
            esac
        done
        printf '%s\\n' "${patterns[*]}" >> "$FAKE_GH_DOWNLOAD_LOG"
        mkdir -p "$destination"
        for pattern in "${patterns[@]}"; do
            cp "$FAKE_GH_FIXTURE/assets/$pattern" "$destination/$pattern"
        done
        ;;
    "api repos/$FAKE_GH_REPOSITORY/commits/$FAKE_GH_TAG")
        printf '%s\\n' "$FAKE_GH_COMMIT"
        ;;
    "api -H")
        cat "$FAKE_GH_FIXTURE/Directory.Build.props"
        ;;
    *)
        printf 'unexpected fake gh arguments: %s\\n' "$*" >&2
        exit 64
        ;;
esac
""",
            encoding="utf-8",
            newline="\n",
        )
        self.fake_gh.chmod(0o755)

        self.environment = os.environ.copy()
        self.environment.update(
            {
                "MEMORIA_NOTE_GH_COMMAND": self.fake_gh.as_posix(),
                "FAKE_GH_REPOSITORY": REPOSITORY,
                "FAKE_GH_FIXTURE": self.fixture.as_posix(),
                "FAKE_GH_TAG": TAG,
                "FAKE_GH_COMMIT": SOURCE_COMMIT,
                "FAKE_GH_DOWNLOAD_LOG": (self.fixture / "downloads.log").as_posix(),
            }
        )

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def write_checksums(self, linux_digest: str | None = None) -> None:
        linux_digest = linux_digest or sha256(self.assets / LINUX_ARCHIVE)
        windows_digest = sha256(self.assets / WINDOWS_ARCHIVE)
        (self.assets / "SHA256SUMS").write_text(
            f"{linux_digest}  {LINUX_ARCHIVE}\n"
            f"{windows_digest}  {WINDOWS_ARCHIVE}\n",
            encoding="utf-8",
            newline="\n",
        )

    def write_release(
        self,
        *,
        draft: bool = False,
        prerelease: bool = False,
        include_windows: bool = True,
    ) -> None:
        names = [LINUX_ARCHIVE]
        if include_windows:
            names.append(WINDOWS_ARCHIVE)
        names.append("SHA256SUMS")
        release = {
            "tagName": TAG,
            "isDraft": draft,
            "isPrerelease": prerelease,
            "assets": [
                {
                    "name": name,
                    "state": "uploaded",
                    "url": (
                        f"https://github.com/{REPOSITORY}/releases/download/{TAG}/{name}"
                    ),
                }
                for name in names
            ],
        }
        (self.fixture / "release.json").write_text(
            json.dumps(release),
            encoding="utf-8",
            newline="\n",
        )

    def run_script(self, tag: str = TAG) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [str(BASH_COMMAND), self.script.as_posix(), tag],
            cwd=self.root,
            env=self.environment,
            check=False,
            capture_output=True,
            text=True,
        )

    def test_generates_both_profiles_and_reuses_identical_outputs(self) -> None:
        first = self.run_script()
        self.assertEqual(first.returncode, 0, first.stderr)

        profile_root = self.root / "performance" / "artifacts"
        linux = json.loads((profile_root / f"{TAG}-linux-x64.json").read_text())
        windows = json.loads((profile_root / f"{TAG}-win-x64.json").read_text())
        self.assertEqual(linux["productVersion"], VERSION)
        self.assertEqual(linux["sourceCommit"], SOURCE_COMMIT)
        self.assertEqual(linux["hostOs"], "linux")
        self.assertEqual(linux["rid"], "linux-x64")
        self.assertEqual(linux["archive"]["sha256"], sha256(self.assets / LINUX_ARCHIVE))
        self.assertEqual(windows["hostOs"], "windows")
        self.assertEqual(windows["rid"], "win-x64")
        self.assertEqual(windows["executableRelativePath"], "mn.exe")
        self.assertEqual(
            windows["archive"]["sha256"], sha256(self.assets / WINDOWS_ARCHIVE)
        )

        cache = self.root / "performance" / ".cache" / "artifacts" / TAG / "download"
        self.assertEqual((cache / LINUX_ARCHIVE).read_bytes(), b"linux archive fixture")
        self.assertEqual((cache / WINDOWS_ARCHIVE).read_bytes(), b"windows archive fixture")
        self.assertTrue((cache / "SHA256SUMS").is_file())

        second = self.run_script()
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertIn("Unchanged", second.stdout)
        download_log = (self.fixture / "downloads.log").read_text(encoding="utf-8")
        self.assertEqual(download_log.count(LINUX_ARCHIVE), 1)
        self.assertEqual(download_log.count(WINDOWS_ARCHIVE), 1)
        self.assertEqual(download_log.count("SHA256SUMS"), 2)

    def test_rejects_draft_and_prerelease(self) -> None:
        for field in ("draft", "prerelease"):
            with self.subTest(field=field):
                self.write_release(**{field: True})
                result = self.run_script()
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(field, result.stderr)
                self.assertFalse(self.profile_path("linux").exists())
                self.write_release()

    def test_rejects_missing_release_asset(self) -> None:
        self.write_release(include_windows=False)

        result = self.run_script()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn(WINDOWS_ARCHIVE, result.stderr)
        self.assertFalse(self.profile_path("linux").exists())

    def test_rejects_downloaded_archive_checksum_mismatch(self) -> None:
        self.write_checksums(linux_digest="0" * 64)

        result = self.run_script()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("checksum mismatch", result.stderr)
        self.assertFalse(self.profile_path("linux").exists())

    def test_rejects_tagged_product_version_mismatch(self) -> None:
        (self.fixture / "Directory.Build.props").write_text(
            "<Project><PropertyGroup>"
            "<MemoriaNoteProductVersion>1.2.4</MemoriaNoteProductVersion>"
            "</PropertyGroup></Project>\n",
            encoding="utf-8",
        )

        result = self.run_script()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("does not match tagged product version", result.stderr)
        self.assertFalse(self.profile_path("linux").exists())

    def test_rejects_conflicting_existing_profile_before_installing_cache(self) -> None:
        profile = self.profile_path("linux")
        profile.parent.mkdir(parents=True)
        profile.write_text("{}\n", encoding="utf-8")

        result = self.run_script()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("already exists with different content", result.stderr)
        cache = self.root / "performance" / ".cache" / "artifacts" / TAG / "download"
        self.assertFalse((cache / LINUX_ARCHIVE).exists())
        self.assertFalse(self.profile_path("windows").exists())

    def test_rejects_mismatched_cached_archive_without_overwriting_it(self) -> None:
        cache = self.root / "performance" / ".cache" / "artifacts" / TAG / "download"
        cache.mkdir(parents=True)
        cached_archive = cache / LINUX_ARCHIVE
        cached_archive.write_bytes(b"unexpected cached bytes")

        result = self.run_script()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("cached archive checksum mismatch", result.stderr)
        self.assertEqual(cached_archive.read_bytes(), b"unexpected cached bytes")
        self.assertFalse(self.profile_path("linux").exists())

    def test_rejects_invalid_tag_before_contacting_github(self) -> None:
        result = self.run_script("1.2.3")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("release tag must use", result.stderr)

    def profile_path(self, host: str) -> Path:
        return self.root / "performance" / "artifacts" / f"{TAG}-{host}-x64.json"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


if __name__ == "__main__":
    unittest.main()
