from __future__ import annotations

import hashlib
import importlib.util
import io
import json
import os
import stat
import subprocess
import sys
import tarfile
import tempfile
import unittest
import zipfile
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest import mock


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "package-release.py"
SPEC = importlib.util.spec_from_file_location("package_release", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load packaging script: {SCRIPT_PATH}")
PACKAGE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = PACKAGE
SPEC.loader.exec_module(PACKAGE)


class ReleaseTagTests(unittest.TestCase):
    def test_normalize_release_tag_removes_prefix(self) -> None:
        self.assertEqual(PACKAGE.normalize_release_tag("v0.2.0"), "0.2.0")
        self.assertEqual(PACKAGE.normalize_release_tag("v12.34.56"), "12.34.56")

    def test_normalize_release_tag_rejects_non_release_versions(self) -> None:
        invalid = (
            "0.2.0",
            "V0.2.0",
            "v0.2",
            "v0.2.0-rc.1",
            "v0.2.0+build",
            "v00.2.0",
            "v0.02.0",
            "v0.2.00",
            " v0.2.0",
        )
        for tag in invalid:
            with self.subTest(tag=tag):
                with self.assertRaises(ValueError):
                    PACKAGE.normalize_release_tag(tag)


class PublishCommandTests(unittest.TestCase):
    def test_release_properties_are_explicit(self) -> None:
        command = PACKAGE.build_publish_command(
            dotnet="dotnet",
            project=Path("cli/mn.csproj"),
            rid="linux-x64",
            version="0.2.0",
            output_directory=Path("out"),
        )

        self.assertEqual(command[:2], ["dotnet", "publish"])
        self.assertIn("linux-x64", command)
        self.assertIn("--self-contained", command)
        self.assertIn("-p:MemoriaNoteProductVersion=0.2.0", command)
        self.assertIn("-p:MemoriaNoteReleaseBuild=true", command)
        self.assertIn("-p:ContinuousIntegrationBuild=true", command)

    def test_names_are_derived_from_tag_and_rid(self) -> None:
        self.assertEqual(
            PACKAGE.archive_name("v0.2.0", "linux-x64"),
            "mn-v0.2.0-linux-x64.tar.gz",
        )
        self.assertEqual(
            PACKAGE.archive_name("v0.2.0", "win-x64"),
            "mn-v0.2.0-win-x64.zip",
        )
        self.assertEqual(
            PACKAGE.archive_root_name("v0.2.0", "linux-x64"),
            "mn-v0.2.0-linux-x64",
        )

    def test_version_execution_requires_matching_x64_host(self) -> None:
        self.assertTrue(
            PACKAGE.host_can_execute(
                "linux-x64", system="Linux", machine="x86_64"
            )
        )
        self.assertTrue(
            PACKAGE.host_can_execute(
                "win-x64", system="Windows", machine="AMD64"
            )
        )
        self.assertFalse(
            PACKAGE.host_can_execute(
                "win-x64", system="Linux", machine="x86_64"
            )
        )
        self.assertFalse(
            PACKAGE.host_can_execute(
                "linux-x64", system="Linux", machine="aarch64"
            )
        )


class ArchiveTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.publish = self.root / "publish"
        self.publish.mkdir()
        self.executable = self.publish / "mn"
        self.executable.write_bytes(b"executable\n")
        (self.publish / "mn.dll").write_bytes(b"managed\n")
        nested = self.publish / "runtimes" / "linux-x64" / "native"
        nested.mkdir(parents=True)
        (nested / "libe_sqlite3.so").write_bytes(b"native\n")
        self.license = self.root / "LICENSE"
        self.license.write_bytes(b"license\n")
        self.archive_root = "mn-v0.2.0-linux-x64"

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def entries(self):
        return PACKAGE.collect_archive_entries(
            publish_directory=self.publish,
            license_path=self.license,
            archive_root=self.archive_root,
            executable=self.executable,
        )

    def test_tar_gz_is_reproducible_and_preserves_executable_mode(self) -> None:
        first = self.root / "first.tar.gz"
        second = self.root / "second.tar.gz"
        PACKAGE.create_tar_gz(first, self.entries())
        os.utime(self.executable, (1_900_000_000, 1_900_000_000))
        PACKAGE.create_tar_gz(second, reversed(self.entries()))

        self.assertEqual(first.read_bytes(), second.read_bytes())
        with tarfile.open(first, "r:gz") as archive:
            names = archive.getnames()
            self.assertEqual(names, sorted(names))
            self.assertIn(f"{self.archive_root}/LICENSE", names)
            executable = archive.getmember(f"{self.archive_root}/mn")
            self.assertEqual(executable.mode, PACKAGE.EXECUTABLE_MODE)
            self.assertEqual(executable.mtime, 0)

    def test_zip_is_reproducible_and_has_single_root(self) -> None:
        first = self.root / "first.zip"
        second = self.root / "second.zip"
        entries = self.entries()
        PACKAGE.create_zip(first, entries)
        os.utime(self.license, (1_900_000_000, 1_900_000_000))
        PACKAGE.create_zip(second, entries)

        self.assertEqual(first.read_bytes(), second.read_bytes())
        with zipfile.ZipFile(first) as archive:
            names = archive.namelist()
            self.assertEqual(names, sorted(names))
            self.assertTrue(
                all(name.rstrip("/").startswith(self.archive_root) for name in names)
            )
            self.assertIn(f"{self.archive_root}/LICENSE", names)
            info = archive.getinfo(f"{self.archive_root}/mn")
            self.assertEqual(
                stat.S_IMODE(info.external_attr >> 16), PACKAGE.EXECUTABLE_MODE
            )
            self.assertEqual(info.date_time, PACKAGE.ZIP_TIMESTAMP)

    def test_checksum_file_is_sorted_and_uses_archive_bytes(self) -> None:
        first = self.root / "mn-v0.2.0-win-x64.zip"
        second = self.root / "mn-v0.2.0-linux-x64.tar.gz"
        first.write_bytes(b"windows")
        second.write_bytes(b"linux")
        artifacts = [
            PACKAGE.ReleaseArtifact(
                "win-x64", "", "", str(first), "", PACKAGE.sha256(first), "executed"
            ),
            PACKAGE.ReleaseArtifact(
                "linux-x64", "", "", str(second), "", PACKAGE.sha256(second), "executed"
            ),
        ]
        checksum_file = self.root / "SHA256SUMS"

        PACKAGE.write_checksum_file(checksum_file, artifacts)

        expected = (
            f"{hashlib.sha256(b'linux').hexdigest()}  {second.name}\n"
            f"{hashlib.sha256(b'windows').hexdigest()}  {first.name}\n"
        )
        self.assertEqual(checksum_file.read_text(encoding="utf-8"), expected)


class OutputSafetyTests(unittest.TestCase):
    def test_clean_directory_removes_stale_content(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "release"
            output.mkdir()
            (output / "stale.txt").write_text("stale", encoding="utf-8")

            PACKAGE.clean_directory(output)

            self.assertEqual(list(output.iterdir()), [])

    def test_clean_directory_rejects_symbolic_link(self) -> None:
        if not hasattr(os, "symlink"):
            self.skipTest("symbolic links are not supported")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "target"
            target.mkdir()
            link = root / "link"
            try:
                link.symlink_to(target, target_is_directory=True)
            except OSError as error:
                self.skipTest(f"cannot create symbolic links: {error}")

            with self.assertRaises(ValueError):
                PACKAGE.clean_directory(link)


class PackagingWorkflowTests(unittest.TestCase):
    def test_package_release_returns_stable_paths_and_cleans_output(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            repository = root / "repository"
            (repository / "cli").mkdir(parents=True)
            (repository / "cli" / "mn.csproj").write_text(
                "<Project />\n", encoding="utf-8"
            )
            (repository / "LICENSE").write_text("license\n", encoding="utf-8")
            output = root / "output"
            stale_directory = output / "v0.2.0"
            stale_directory.mkdir(parents=True)
            (stale_directory / "stale.txt").write_text("stale", encoding="utf-8")

            def fake_runner(command, working_directory):
                self.assertTrue(os.path.samefile(working_directory, repository))
                if command[1] == "publish":
                    publish = Path(command[command.index("--output") + 1])
                    publish.mkdir(parents=True, exist_ok=True)
                    for name in (
                        "mn",
                        "mn.dll",
                        "mn.deps.json",
                        "mn.runtimeconfig.json",
                    ):
                        (publish / name).write_text(name, encoding="utf-8")
                    return subprocess.CompletedProcess(command, 0, "publish", "")
                return subprocess.CompletedProcess(command, 0, "mn 0.2.0\n", "")

            with (
                mock.patch.object(PACKAGE, "host_can_execute", return_value=True),
                redirect_stdout(io.StringIO()),
                redirect_stderr(io.StringIO()),
            ):
                result = PACKAGE.package_release(
                    tag="v0.2.0",
                    rids=("linux-x64",),
                    output_directory=output,
                    repository_root=repository,
                    dotnet="dotnet",
                    runner=fake_runner,
                )

            release_directory = output / "v0.2.0"
            result_file = release_directory / "package-results.json"
            self.assertFalse((release_directory / "stale.txt").exists())
            self.assertEqual(result["schema_version"], 1)
            self.assertEqual(result["version"], "0.2.0")
            self.assertEqual(result["result_file"], str(result_file.resolve()))
            self.assertEqual(
                json.loads(result_file.read_text(encoding="utf-8")), result
            )
            artifact = result["artifacts"][0]
            self.assertEqual(artifact["version_check"], "executed")
            self.assertTrue(Path(artifact["publish_directory"]).is_absolute())
            self.assertTrue(Path(artifact["executable"]).is_file())
            self.assertTrue(Path(artifact["archive"]).is_file())
            self.assertTrue(Path(result["checksum_file"]).is_file())


if __name__ == "__main__":
    unittest.main()
