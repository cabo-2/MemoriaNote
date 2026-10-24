from __future__ import annotations

import hashlib
import io
import json
import os
import sys
import tarfile
import tempfile
import unittest
import zipfile
from argparse import Namespace
from datetime import datetime, timezone
from pathlib import Path
from unittest import mock


SCRIPT_DIRECTORY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_DIRECTORY))

import checkpoint  # noqa: E402


class ArtifactProfileTests(unittest.TestCase):
    def test_checked_in_profiles_match_supported_hosts(self) -> None:
        cases = (
            ("v0.2.0-linux-x64.json", checkpoint.HostIdentity("linux", "x64")),
            ("v0.2.0-win-x64.json", checkpoint.HostIdentity("windows", "x64")),
        )
        for file_name, host in cases:
            with self.subTest(file_name=file_name):
                path = checkpoint.REPOSITORY_ROOT / "performance/artifacts" / file_name
                profile = checkpoint.load_artifact_profile(path)
                checkpoint.validate_profile_host(profile, host)

    def test_profile_rejects_unknown_field(self) -> None:
        profile = sample_profile()
        profile["unexpected"] = True
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "profile.json"
            path.write_text(json.dumps(profile), encoding="utf-8")
            with self.assertRaisesRegex(checkpoint.WorkflowError, "unknown fields"):
                checkpoint.load_artifact_profile(path)

    def test_profile_rejects_version_that_could_escape_cache(self) -> None:
        profile = sample_profile()
        profile["productVersion"] = "../outside"
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "profile.json"
            path.write_text(json.dumps(profile), encoding="utf-8")
            with self.assertRaisesRegex(checkpoint.WorkflowError, "SemVer"):
                checkpoint.load_artifact_profile(path)

    def test_profile_rejects_host_mismatch(self) -> None:
        with self.assertRaisesRegex(checkpoint.WorkflowError, "requires linux"):
            checkpoint.validate_profile_host(
                sample_profile(), checkpoint.HostIdentity("windows", "x64")
            )


class HostDetectionTests(unittest.TestCase):
    def test_normalizes_linux_and_windows(self) -> None:
        self.assertEqual(
            checkpoint.detect_host("Linux", "x86_64"),
            checkpoint.HostIdentity("linux", "x64"),
        )
        self.assertEqual(
            checkpoint.detect_host("Windows", "AMD64"),
            checkpoint.HostIdentity("windows", "x64"),
        )

    def test_rejects_unsupported_operating_system(self) -> None:
        with self.assertRaisesRegex(checkpoint.WorkflowError, "Unsupported host"):
            checkpoint.detect_host("Darwin", "arm64")


class RepositoryPathTests(unittest.TestCase):
    def test_normalizes_repository_root_before_relativizing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repository_root = Path(temporary).resolve()
            aliased_root = repository_root / "unused" / ".."
            target = repository_root / "performance" / "sample.json"
            target.parent.mkdir()
            target.write_text("{}", encoding="utf-8")

            with mock.patch.object(checkpoint, "REPOSITORY_ROOT", aliased_root):
                relative = checkpoint.repository_relative(target)

        self.assertEqual(relative, "performance/sample.json")


class ArtifactCacheTests(unittest.TestCase):
    def test_downloads_once_and_reuses_matching_cache(self) -> None:
        payload = b"small artifact fixture"
        profile = sample_profile(payload)
        calls = []

        def download(url: str, destination: io.BufferedWriter) -> None:
            calls.append(url)
            destination.write(payload)

        with tempfile.TemporaryDirectory() as temporary:
            cache_root = Path(temporary)
            first = checkpoint.ensure_downloaded_artifact(
                profile, cache_root, download=download
            )
            second = checkpoint.ensure_downloaded_artifact(
                profile, cache_root, download=download
            )
            self.assertEqual(first, second)
            self.assertEqual(first.read_bytes(), payload)
            self.assertEqual(calls, [profile["archive"]["url"]])

    def test_rejects_existing_mismatched_cache_without_downloading(self) -> None:
        profile = sample_profile(b"expected")
        with tempfile.TemporaryDirectory() as temporary:
            cache_root = Path(temporary)
            destination = (
                cache_root
                / "artifacts"
                / "v1.0.0"
                / "download"
                / profile["archive"]["fileName"]
            )
            destination.parent.mkdir(parents=True)
            destination.write_bytes(b"wrong")

            def unexpected_download(url: str, output: io.BufferedWriter) -> None:
                self.fail("download must not run for a mismatched existing cache")

            with self.assertRaisesRegex(checkpoint.WorkflowError, "checksum mismatch"):
                checkpoint.ensure_downloaded_artifact(
                    profile, cache_root, download=unexpected_download
                )
            self.assertEqual(destination.read_bytes(), b"wrong")

    def test_rejects_mismatched_download_without_publishing_cache(self) -> None:
        profile = sample_profile(b"expected")
        with tempfile.TemporaryDirectory() as temporary:
            cache_root = Path(temporary)

            def download(url: str, destination: io.BufferedWriter) -> None:
                destination.write(b"wrong")

            with self.assertRaisesRegex(checkpoint.WorkflowError, "checksum mismatch"):
                checkpoint.ensure_downloaded_artifact(
                    profile, cache_root, download=download
                )
            destination = (
                cache_root
                / "artifacts"
                / "v1.0.0"
                / "download"
                / profile["archive"]["fileName"]
            )
            self.assertFalse(destination.exists())


class ArchiveExtractionTests(unittest.TestCase):
    def test_extracts_small_tar_and_preserves_executable_mode(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive_path = root / "sample.tar.gz"
            content = b"#!/bin/sh\n"
            with tarfile.open(archive_path, "w:gz") as archive:
                member = tarfile.TarInfo("sample-root/mn")
                member.size = len(content)
                member.mode = 0o755
                archive.addfile(member, io.BytesIO(content))
            destination = root / "out"
            destination.mkdir()
            checkpoint.extract_tar_gz(archive_path, destination, "sample-root")
            executable = destination / "sample-root/mn"
            self.assertEqual(executable.read_bytes(), content)
            if os.name != "nt":
                self.assertNotEqual(executable.stat().st_mode & 0o100, 0)

    def test_extracts_small_zip(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive_path = root / "sample.zip"
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr("sample-root/mn.exe", b"fixture")
            destination = root / "out"
            destination.mkdir()
            checkpoint.extract_zip(archive_path, destination, "sample-root")
            self.assertEqual(
                (destination / "sample-root/mn.exe").read_bytes(), b"fixture"
            )

    def test_rejects_parent_traversal(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive_path = root / "unsafe.tar.gz"
            content = b"unsafe"
            with tarfile.open(archive_path, "w:gz") as archive:
                member = tarfile.TarInfo("sample-root/../outside")
                member.size = len(content)
                archive.addfile(member, io.BytesIO(content))
            destination = root / "out"
            destination.mkdir()
            with self.assertRaisesRegex(checkpoint.WorkflowError, "unsafe path"):
                checkpoint.extract_tar_gz(archive_path, destination, "sample-root")
            self.assertFalse((root / "outside").exists())

    def test_rejects_windows_style_traversal(self) -> None:
        with self.assertRaisesRegex(checkpoint.WorkflowError, "unsafe path"):
            checkpoint.safe_member_parts("sample-root\\..\\outside", "sample-root")


class DatasetPreparationTests(unittest.TestCase):
    def test_existing_dataset_is_verified_without_prepare(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            manifest_path, prepared_root = write_dataset_manifest(root)
            prepared_root.mkdir(parents=True)
            commands = []

            def command(arguments, working_directory):
                commands.append(list(arguments))
                self.assertIn("--no-restore", arguments)
                return checkpoint.CommandResult(0, "verified\n", "")

            checkpoint.prepare_dataset(manifest_path, root / "cache", command=command)
            self.assertEqual(command_action(commands[0]), "verify")
            self.assertEqual(len(commands), 1)

    def test_missing_dataset_is_prepared_then_verified(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            manifest_path, _ = write_dataset_manifest(root)
            commands = []

            def command(arguments, working_directory):
                commands.append(list(arguments))
                self.assertIn("--no-restore", arguments)
                return checkpoint.CommandResult(0, "ok\n", "")

            checkpoint.prepare_dataset(manifest_path, root / "cache", command=command)
            self.assertEqual([command_action(value) for value in commands], ["prepare", "verify"])

    def test_invalid_existing_dataset_is_not_reprepared(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            manifest_path, prepared_root = write_dataset_manifest(root)
            prepared_root.mkdir(parents=True)
            commands = []

            def command(arguments, working_directory):
                commands.append(list(arguments))
                self.assertIn("--no-restore", arguments)
                return checkpoint.CommandResult(1, "checksum mismatch\n", "")

            with self.assertRaisesRegex(checkpoint.WorkflowError, "verification"):
                checkpoint.prepare_dataset(manifest_path, root / "cache", command=command)
            self.assertEqual([command_action(value) for value in commands], ["verify"])


class PrepareWorkflowTests(unittest.TestCase):
    def test_restores_before_preparing_inputs(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repository_root = Path(temporary)
            profile_path = repository_root / "performance/artifacts/sample.json"
            profile_path.parent.mkdir(parents=True)
            profile_path.write_text(json.dumps(sample_profile()), encoding="utf-8")
            events = []

            def command(arguments, working_directory):
                events.append(tuple(arguments))
                return checkpoint.CommandResult(0, "restored\n", "")

            def prepare_dataset(manifest_path, cache_root):
                events.append(("prepare-dataset", manifest_path.name))

            def prepare_artifact(profile, cache_root):
                events.append(("prepare-artifact", profile["artifactId"]))
                return cache_root / "artifact"

            arguments = Namespace(
                artifact_profile="performance/artifacts/sample.json",
                artifact_input=None,
                cache_root="performance/.cache",
            )
            with (
                mock.patch.object(checkpoint, "REPOSITORY_ROOT", repository_root),
                mock.patch.object(
                    checkpoint,
                    "detect_host",
                    return_value=checkpoint.HostIdentity("linux", "x64"),
                ),
                mock.patch.object(checkpoint, "run_command", side_effect=command),
                mock.patch.object(
                    checkpoint, "prepare_dataset", side_effect=prepare_dataset
                ),
                mock.patch.object(
                    checkpoint,
                    "ensure_downloaded_artifact",
                    side_effect=prepare_artifact,
                ),
            ):
                checkpoint.prepare_workflow(arguments)

            self.assertEqual(events[0], ("dotnet", "restore", "MemoriaNote.sln"))
            self.assertEqual(
                [event[0] for event in events[1:]],
                ["prepare-dataset", "prepare-dataset", "prepare-artifact"],
            )


class ReportTests(unittest.TestCase):
    def test_default_report_path_uses_local_reports_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repository_root = Path(temporary)
            with mock.patch.object(checkpoint, "REPOSITORY_ROOT", repository_root):
                path = checkpoint.default_report_path(sample_session())

        self.assertEqual(
            path,
            repository_root
            / "performance/reports/ecli-1010c/20261024T010203Z-abcdef.md",
        )

    def test_renders_english_report_from_small_result_fixture(self) -> None:
        session = sample_session()
        results = [sample_result(), sample_result(), sample_result()]
        report = checkpoint.render_report(session, results)
        self.assertIn("# v0.2.0 initial baseline", report)
        self.assertIn("## Reference environment", report)
        self.assertIn("## Validated results", report)
        self.assertIn("| 1.500 | 2.500 | 1.000 | 3.000 | 1.750 |", report)
        self.assertNotIn("/home/", report)

    def test_session_rejects_unknown_field(self) -> None:
        session = sample_session()
        session["unexpected"] = "value"
        with self.assertRaisesRegex(checkpoint.WorkflowError, "unknown fields"):
            checkpoint.validate_session_record(session)

    def test_report_rejects_mixed_environment(self) -> None:
        session = sample_session()
        first = sample_result()
        second = sample_result()
        third = sample_result()
        second["environment"]["cpuModel"] = "Different CPU"
        with self.assertRaisesRegex(checkpoint.WorkflowError, "one identity"):
            checkpoint.render_report(session, [first, second, third])


class WorkflowIntegrationTests(unittest.TestCase):
    def test_small_fixture_runs_session_and_generates_report(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            repository_root = Path(temporary)
            profile_path, archive_path = write_workflow_fixture(repository_root)
            fixed_time = datetime(2026, 10, 24, 1, 2, 3, tzinfo=timezone.utc)

            def command(arguments, working_directory):
                values = list(arguments)
                if values[:2] == ["dotnet", "--version"]:
                    return checkpoint.CommandResult(0, "10.0.100\n", "")
                if values[0] == "dotnet" and values[1] == "build":
                    self.assertIn("--no-restore", values)
                    return checkpoint.CommandResult(0, "build succeeded\n", "")
                if Path(values[0]).name == "mn" and values[1:] == ["--version"]:
                    return checkpoint.CommandResult(0, "mn 1.0.0\n", "")
                separator = values.index("--")
                action = values[separator + 1]
                if action == "describe":
                    return checkpoint.CommandResult(0, "description\n", "")
                if action == "run":
                    output = Path(values[values.index("--output") + 1])
                    output.parent.mkdir(parents=True, exist_ok=True)
                    result = sample_result()
                    result["environment"]["environmentLabel"] = "fixture-environment"
                    output.write_text(json.dumps(result), encoding="utf-8")
                    return checkpoint.CommandResult(0, "valid result\n", "")
                if action == "validate-result":
                    return checkpoint.CommandResult(0, "validated\n", "")
                raise AssertionError(f"Unexpected command: {values}")

            run_arguments = Namespace(
                artifact_profile=str(profile_path.relative_to(repository_root)),
                artifact_input=str(archive_path),
                roadmap_id="ECLI-1010c",
                description="Small fixture checkpoint",
                environment_label="fixture-environment",
                power_mode_note=None,
                cache_root="performance/.cache",
                results_root="performance/results",
                temporary_root="performance/.tmp/checkpoints",
                keep_temporary=False,
            )

            with (
                mock.patch.object(checkpoint, "REPOSITORY_ROOT", repository_root),
                mock.patch.object(checkpoint, "run_command", side_effect=command),
                mock.patch.object(
                    checkpoint,
                    "verify_dataset",
                    return_value=repository_root / "performance/.cache/prepared",
                ),
                mock.patch.object(
                    checkpoint,
                    "detect_host",
                    return_value=checkpoint.HostIdentity("linux", "x64"),
                ),
                mock.patch.object(checkpoint, "utc_now", return_value=fixed_time),
                mock.patch.object(checkpoint.os, "access", return_value=True),
            ):
                session_path = checkpoint.run_workflow(run_arguments)
                session = checkpoint.read_json(session_path, "Checkpoint session")
                self.assertEqual(session["status"], "valid")
                self.assertEqual(len(session["results"]), 3)
                self.assertFalse(
                    (repository_root / "performance/.tmp/checkpoints" / session["sessionId"]).exists()
                )

                output_path = repository_root / "generated-report.md"
                report_arguments = Namespace(
                    session=str(session_path), output=str(output_path)
                )
                report_path = checkpoint.report_workflow(report_arguments)
                self.assertEqual(report_path, output_path)
                report = report_path.read_text(encoding="utf-8")
                self.assertIn("# Small fixture checkpoint", report)
                self.assertEqual(report.count("startup-version.extracted-archive"), 3)
                with self.assertRaisesRegex(checkpoint.WorkflowError, "already exists"):
                    checkpoint.report_workflow(report_arguments)


def sample_profile(payload: bytes = b"fixture") -> dict:
    return {
        "schemaVersion": "1",
        "artifactId": "sample-linux-x64-self-contained",
        "productVersion": "1.0.0",
        "sourceCommit": "1" * 40,
        "hostOs": "linux",
        "rid": "linux-x64",
        "configuration": "Release",
        "deploymentKind": "self-contained",
        "artifactInputKind": "extracted-archive",
        "executableRelativePath": "mn",
        "expectedVersionOutput": "mn 1.0.0",
        "archive": {
            "format": "tar.gz",
            "fileName": "sample.tar.gz",
            "url": "https://example.invalid/sample.tar.gz",
            "sha256": hashlib.sha256(payload).hexdigest(),
            "rootDirectory": "sample-root",
        },
    }


def write_dataset_manifest(root: Path) -> tuple[Path, Path]:
    manifest = {
        "datasetId": "sample",
        "datasetVersion": "1",
        "prepared": {"relativePath": "prepared"},
    }
    manifest_path = root / "dataset-manifest.json"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    prepared_root = root / "cache/datasets/sample/1/prepared"
    return manifest_path, prepared_root


def write_workflow_fixture(repository_root: Path) -> tuple[Path, Path]:
    for spec in checkpoint.RUN_SPECS:
        scenario_path = repository_root / spec.scenario_manifest
        scenario_path.parent.mkdir(parents=True, exist_ok=True)
        scenario_path.write_text("{}\n", encoding="utf-8")
        if spec.dataset_manifest is not None:
            dataset_path = repository_root / spec.dataset_manifest
            dataset_path.parent.mkdir(parents=True, exist_ok=True)
            dataset_id = spec.name
            dataset_path.write_text(
                json.dumps(
                    {
                        "datasetId": dataset_id,
                        "datasetVersion": "1",
                        "prepared": {"relativePath": "prepared"},
                    }
                ),
                encoding="utf-8",
            )

    archive_path = repository_root / "sample.tar.gz"
    content = b"#!/bin/sh\n"
    with tarfile.open(archive_path, "w:gz") as archive:
        member = tarfile.TarInfo("sample-root/mn")
        member.size = len(content)
        member.mode = 0o755
        archive.addfile(member, io.BytesIO(content))

    profile = sample_profile(archive_path.read_bytes())
    profile_path = repository_root / "performance/artifacts/sample.json"
    profile_path.parent.mkdir(parents=True, exist_ok=True)
    profile_path.write_text(json.dumps(profile), encoding="utf-8")
    return profile_path, archive_path


def command_action(arguments: list[str]) -> str:
    separator = arguments.index("--")
    return arguments[separator + 1]


def sample_session() -> dict:
    return {
        "schemaVersion": "1",
        "sessionId": "20261024T010203Z-abcdef",
        "roadmapId": "ECLI-1010c",
        "description": "v0.2.0 initial baseline",
        "environmentLabel": "linux-reference-1",
        "host": {"os": "linux", "architecture": "x64"},
        "startedAtUtc": "2026-10-24T01:02:03Z",
        "endedAtUtc": "2026-10-24T01:12:03Z",
        "status": "valid",
        "dotnetSdkVersion": "10.0.100",
        "runner": {"version": "1.0.0", "commit": "4" * 40},
        "artifact": {
            "artifactId": "sample-linux-x64-self-contained",
            "productVersion": "1.0.0",
            "sourceCommit": "1" * 40,
            "rid": "linux-x64",
            "configuration": "Release",
            "deploymentKind": "self-contained",
            "artifactInputKind": "extracted-archive",
        },
        "inputs": [
            {
                "kind": "artifact-profile",
                "path": "performance/artifacts/sample.json",
                "sha256": "2" * 64,
            }
        ],
        "results": [
            {
                "name": name,
                "path": f"performance/results/ecli-1010c/sample/{name}.json",
                "sha256": "3" * 64,
                "status": "valid",
                "validated": True,
            }
            for name in ("generic", "dotnet-docs-en", "mdn-translated-content-ja")
        ],
        "failures": [],
    }


def sample_result() -> dict:
    return {
        "schemaVersion": "1",
        "run": {
            "runId": "run-1",
            "startedAtUtc": "2026-10-24T01:02:03Z",
            "runnerVersion": "1.0.0",
            "runnerCommit": "4" * 40,
            "status": "valid",
        },
        "target": {
            "artifactId": "sample-linux-x64-self-contained",
            "productVersion": "1.0.0",
            "sourceCommit": "1" * 40,
            "rid": "linux-x64",
            "configuration": "Release",
            "deploymentKind": "self-contained",
            "artifactInputKind": "extracted-archive",
            "executableSha256": "5" * 64,
            "executableSizeBytes": 1234,
            "archiveSha256": "6" * 64,
        },
        "environment": {
            "environmentLabel": "linux-reference-1",
            "osDescription": "Sample Linux",
            "osArchitecture": "X64",
            "processArchitecture": "X64",
            "cpuModel": "Sample CPU",
            "logicalProcessorCount": 4,
            "installedMemoryBytes": 8 * 1024**3,
            "fileSystem": "ext4",
        },
        "scenarioResults": [
            {
                "scenarioId": "startup-version.extracted-archive",
                "scenarioVersion": "1",
                "cacheMode": "process-cold",
                "warmupCount": 1,
                "measurementCount": 3,
                "timeoutMilliseconds": 1000,
                "status": "valid",
                "samples": [],
                "summary": {
                    "sampleCount": 3,
                    "percentileMethod": "nearest-rank",
                    "p50Nanoseconds": 1_500_000,
                    "p95Nanoseconds": 2_500_000,
                    "minNanoseconds": 1_000_000,
                    "maxNanoseconds": 3_000_000,
                    "arithmeticMeanNanoseconds": 1_750_000,
                },
                "validation": {"isValid": True, "issues": []},
            }
        ],
        "failures": [],
    }


if __name__ == "__main__":
    unittest.main()
