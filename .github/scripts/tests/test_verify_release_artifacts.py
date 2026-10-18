from __future__ import annotations

import importlib.util
import io
import json
import os
import stat
import sys
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path


SCRIPTS_DIRECTORY = Path(__file__).resolve().parents[1]


def load_script(module_name: str, file_name: str):
    path = SCRIPTS_DIRECTORY / file_name
    spec = importlib.util.spec_from_file_location(module_name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load script: {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


VERIFY = load_script("verify_release_artifacts", "verify-release-artifacts.py")
PACKAGE = load_script("package_release_for_verification", "package-release.py")


class PackageFixture:
    def __init__(self, root: Path, rid: str = "linux-x64") -> None:
        self.root = root
        self.rid = rid
        self.tag = "v0.0.0"
        self.version = "0.0.0"
        self.release = root / self.tag
        self.publish = self.release / "publish" / rid
        self.publish.mkdir(parents=True)
        executable_name = "mn" if rid == "linux-x64" else "mn.exe"
        self.executable = self.publish / executable_name
        for name in VERIFY.required_publish_files(rid):
            path = self.publish / name
            path.write_bytes(f"fixture:{name}\n".encode("utf-8"))
        if rid == "linux-x64":
            self.executable.chmod(0o755)
        self.license = root / "LICENSE"
        self.license.write_text("license\n", encoding="utf-8")
        self.archive_root = f"mn-{self.tag}-{rid}"
        self.archive = self.release / PACKAGE.archive_name(self.tag, rid)
        entries = PACKAGE.collect_archive_entries(
            publish_directory=self.publish,
            license_path=self.license,
            archive_root=self.archive_root,
            executable=self.executable,
        )
        if rid == "linux-x64":
            PACKAGE.create_tar_gz(self.archive, entries)
        else:
            PACKAGE.create_zip(self.archive, entries)
        self.digest = PACKAGE.sha256(self.archive)
        self.checksum_file = self.release / "SHA256SUMS"
        self.checksum_file.write_text(
            f"{self.digest}  {self.archive.name}\n",
            encoding="utf-8",
            newline="\n",
        )
        self.result_file = self.release / "package-results.json"
        self.write_results()

    def write_results(self, **artifact_overrides) -> None:
        artifact = {
            "rid": self.rid,
            "publish_directory": str(self.publish.resolve()),
            "executable": str(self.executable.resolve()),
            "archive": str(self.archive.resolve()),
            "archive_root": self.archive_root,
            "sha256": self.digest,
            "version_check": "executed",
        }
        artifact.update(artifact_overrides)
        result = {
            "schema_version": 1,
            "tag": self.tag,
            "version": self.version,
            "output_directory": str(self.release.resolve()),
            "checksum_file": str(self.checksum_file.resolve()),
            "result_file": str(self.result_file.resolve()),
            "artifacts": [artifact],
        }
        self.result_file.write_text(
            json.dumps(result, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )


class VersionContractTests(unittest.TestCase):
    def test_expected_version_accepts_three_numeric_components(self) -> None:
        VERIFY.validate_expected_version("0.0.0")
        VERIFY.validate_expected_version("12.34.56")

    def test_expected_version_rejects_non_release_versions(self) -> None:
        for version in ("v0.2.0", "0.2", "0.2.0-dev", "00.2.0", "0.02.0"):
            with self.subTest(version=version):
                with self.assertRaises(VERIFY.VerificationError):
                    VERIFY.validate_expected_version(version)


class ProcessContractTests(unittest.TestCase):
    def test_run_command_keeps_raw_standard_streams(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            script = Path(directory) / "streams.py"
            script.write_text(
                "import sys\n"
                "sys.stdout.buffer.write('日本語'.encode('utf-8'))\n"
                "sys.stderr.buffer.write(b'diagnostic')\n"
                "raise SystemExit(4)\n",
                encoding="utf-8",
            )
            result = VERIFY.run_command(
                executable=Path(sys.executable),
                arguments=(str(script),),
                working_directory=Path(directory),
                environment=os.environ.copy(),
                timeout_seconds=5,
            )

            self.assertEqual(result.exit_code, 4)
            self.assertEqual(result.standard_output, "日本語".encode("utf-8"))
            self.assertEqual(result.standard_error, b"diagnostic")

    def test_command_failure_contains_command_and_both_streams(self) -> None:
        result = VERIFY.CommandResult(("mn", "bad"), 2, b"out", b"error")

        with self.assertRaisesRegex(
            VERIFY.VerificationError,
            "returned exit code 2; expected 0",
        ) as caught:
            VERIFY.require_exit_code(result, 0)

        self.assertIn("mn bad", str(caught.exception))
        self.assertIn("out", str(caught.exception))
        self.assertIn("error", str(caught.exception))


class TemporaryStateTests(unittest.TestCase):
    def test_success_removes_temporary_state(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory)

            VERIFY.execute_with_temporary_root(
                parent=parent,
                cleanup_on_failure=False,
                action=lambda root: (root / "result.txt").write_text(
                    "ok", encoding="utf-8"
                ),
            )

            self.assertEqual(list(parent.iterdir()), [])

    def test_failure_preserves_state_by_default(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory)

            def fail(root: Path) -> None:
                (root / "diagnostic.txt").write_text("keep", encoding="utf-8")
                raise VERIFY.VerificationError("scenario failed")

            with self.assertRaisesRegex(
                VERIFY.VerificationError,
                "Temporary verification state preserved",
            ):
                VERIFY.execute_with_temporary_root(
                    parent=parent,
                    cleanup_on_failure=False,
                    action=fail,
                )

            children = list(parent.iterdir())
            self.assertEqual(len(children), 1)
            self.assertTrue((children[0] / "diagnostic.txt").is_file())

    def test_cleanup_on_failure_removes_state(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory)

            with self.assertRaisesRegex(VERIFY.VerificationError, "failed"):
                VERIFY.execute_with_temporary_root(
                    parent=parent,
                    cleanup_on_failure=True,
                    action=lambda root: (_ for _ in ()).throw(
                        VERIFY.VerificationError("failed")
                    ),
                )

            self.assertEqual(list(parent.iterdir()), [])


class PackageContractTests(unittest.TestCase):
    def test_native_package_is_checked_extracted_and_smoked_twice(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            native_rid = "win-x64" if os.name == "nt" else "linux-x64"
            fixture = PackageFixture(root, rid=native_rid)
            smoke_calls = []

            def fake_smoke(executable, rid, version, smoke_root, timeout):
                smoke_calls.append((executable, rid, version, smoke_root, timeout))
                VERIFY.validate_published_layout(executable, rid)

            verification_root = root / "verification"
            verification_root.mkdir()
            VERIFY.verify_package(
                package_results=fixture.result_file,
                publish_executable=fixture.executable,
                rid=fixture.rid,
                expected_version=fixture.version,
                root=verification_root,
                timeout_seconds=7,
                smoke_runner=fake_smoke,
            )

            self.assertEqual(len(smoke_calls), 2)
            self.assertTrue(os.path.samefile(smoke_calls[0][0], fixture.executable))
            extracted = smoke_calls[1][0]
            self.assertTrue(extracted.is_file())
            self.assertIn("archive extraction", str(extracted))
            self.assertEqual(smoke_calls[1][1:3], (native_rid, "0.0.0"))
            if native_rid == "linux-x64":
                self.assertNotEqual(extracted.stat().st_mode & 0o111, 0)
            else:
                self.assertTrue((extracted.parent / "hostpolicy.dll").is_file())

    def test_windows_zip_layout_is_inspected_and_extracted(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = PackageFixture(root, rid="win-x64")
            artifact = VERIFY.read_package_artifact(
                package_results=fixture.result_file,
                rid=fixture.rid,
                expected_version=fixture.version,
                publish_executable=fixture.executable,
            )

            VERIFY.verify_checksum(artifact)
            members = VERIFY.inspect_archive(artifact)
            extracted = VERIFY.extract_archive(
                artifact,
                members,
                root / "extracted",
            )

            self.assertTrue((extracted / "mn.exe").is_file())
            self.assertTrue((extracted / "hostpolicy.dll").is_file())
            self.assertTrue((extracted / "e_sqlite3.dll").is_file())

    def test_explicit_executable_must_match_package_results(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture = PackageFixture(root)
            other = root / "other-mn"
            other.write_bytes(b"other")

            with self.assertRaisesRegex(
                VERIFY.VerificationError,
                "does not match package results",
            ):
                VERIFY.read_package_artifact(
                    package_results=fixture.result_file,
                    rid=fixture.rid,
                    expected_version=fixture.version,
                    publish_executable=other,
                )

    def test_checksum_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            fixture = PackageFixture(Path(directory))
            artifact = VERIFY.read_package_artifact(
                package_results=fixture.result_file,
                rid=fixture.rid,
                expected_version=fixture.version,
                publish_executable=fixture.executable,
            )
            fixture.archive.write_bytes(fixture.archive.read_bytes() + b"changed")

            with self.assertRaisesRegex(VERIFY.VerificationError, "SHA-256"):
                VERIFY.verify_checksum(artifact)

    def test_package_version_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            fixture = PackageFixture(Path(directory))

            with self.assertRaisesRegex(VERIFY.VerificationError, "version/tag"):
                VERIFY.read_package_artifact(
                    package_results=fixture.result_file,
                    rid=fixture.rid,
                    expected_version="1.2.3",
                    publish_executable=fixture.executable,
                )


class ArchiveSafetyTests(unittest.TestCase):
    def test_tar_path_traversal_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive_path = Path(directory) / "unsafe.tar.gz"
            with tarfile.open(archive_path, "w:gz") as archive:
                info = tarfile.TarInfo("root/../outside")
                info.size = 1
                archive.addfile(info, io.BytesIO(b"x"))

            with self.assertRaisesRegex(VERIFY.VerificationError, "unsafe path"):
                VERIFY.inspect_tar_archive(archive_path, "root")

    def test_non_normalized_archive_path_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive_path = Path(directory) / "unsafe.zip"
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr("root//file", b"x")

            with self.assertRaisesRegex(
                VERIFY.VerificationError,
                "non-normalized path",
            ):
                VERIFY.inspect_zip_archive(archive_path, "root")

    def test_tar_symbolic_link_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive_path = Path(directory) / "link.tar.gz"
            with tarfile.open(archive_path, "w:gz") as archive:
                info = tarfile.TarInfo("root/link")
                info.type = tarfile.SYMTYPE
                info.linkname = "target"
                archive.addfile(info)

            with self.assertRaisesRegex(
                VERIFY.VerificationError,
                "symbolic or hard link",
            ):
                VERIFY.inspect_tar_archive(archive_path, "root")

    def test_zip_symbolic_link_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive_path = Path(directory) / "link.zip"
            with zipfile.ZipFile(archive_path, "w") as archive:
                info = zipfile.ZipInfo("root/link")
                info.create_system = 3
                info.external_attr = (stat.S_IFLNK | 0o777) << 16
                archive.writestr(info, b"target")

            with self.assertRaisesRegex(VERIFY.VerificationError, "symbolic link"):
                VERIFY.inspect_zip_archive(archive_path, "root")


class BackupManifestTests(unittest.TestCase):
    def test_backup_creator_version_is_checked(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "backup.zip"
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr(
                    "manifest.json",
                    json.dumps({"createdBy": {"version": "0.2.0"}}),
                )

            VERIFY.verify_backup_version(path, "0.2.0")
            with self.assertRaisesRegex(
                VERIFY.VerificationError,
                "expected '1.0.0'",
            ):
                VERIFY.verify_backup_version(path, "1.0.0")


if __name__ == "__main__":
    unittest.main()
