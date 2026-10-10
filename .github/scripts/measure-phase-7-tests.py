#!/usr/bin/env python3
"""Measure Phase 7 test suites and write normalized TRX statistics as JSON."""

from __future__ import annotations

import argparse
import json
import os
import platform
import statistics
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Sequence


SCHEMA_VERSION = 1
DEFAULT_TARGET = "MemoriaNote.sln"
DEFAULT_OUTPUT_DIRECTORY = "artifacts/test-measurements"
SUITE_FILTERS = {
    "fast": "TestCategory!=Process",
    "process": "TestCategory=Process",
    "full": None,
    "cli-integration": "TestCategory=CliIntegration",
}


@dataclass(frozen=True)
class TestCounts:
    discovered: int
    passed: int
    failed: int
    skipped: int

    def __add__(self, other: "TestCounts") -> "TestCounts":
        return TestCounts(
            discovered=self.discovered + other.discovered,
            passed=self.passed + other.passed,
            failed=self.failed + other.failed,
            skipped=self.skipped + other.skipped,
        )


def parse_arguments(arguments: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Run one Phase 7 test suite repeatedly, parse its TRX files, and "
            "write environment, counts, and command wall time to JSON."
        )
    )
    parser.add_argument(
        "--stage",
        required=True,
        choices=("baseline", "after"),
        help="Whether the result describes the migration baseline or final state.",
    )
    parser.add_argument(
        "--suite",
        required=True,
        help=(
            "Measurement label. fast, process, full, and cli-integration have "
            "built-in filters; other labels require --filter."
        ),
    )
    parser.add_argument(
        "--filter",
        help="Override the built-in VSTest filter, or define one for a custom suite.",
    )
    parser.add_argument(
        "--target",
        default=DEFAULT_TARGET,
        help=f"Solution or test project passed to dotnet test (default: {DEFAULT_TARGET}).",
    )
    parser.add_argument(
        "--configuration",
        default="Release",
        help="Build configuration passed to dotnet test (default: Release).",
    )
    parser.add_argument(
        "--warm-up",
        type=non_negative_integer,
        default=1,
        help="Number of unmeasured warm-up runs (default: 1).",
    )
    parser.add_argument(
        "--repeat",
        type=positive_integer,
        default=3,
        help="Number of measured runs used for the median (default: 3).",
    )
    parser.add_argument(
        "--output-directory",
        default=DEFAULT_OUTPUT_DIRECTORY,
        help=(
            "Directory for raw TRX files and normalized JSON "
            f"(default: {DEFAULT_OUTPUT_DIRECTORY})."
        ),
    )
    parser.add_argument(
        "--dotnet",
        default=os.environ.get("DOTNET_HOST_PATH", "dotnet"),
        help="dotnet host to execute (default: DOTNET_HOST_PATH or dotnet).",
    )
    parsed = parser.parse_args(arguments)
    if parsed.suite not in SUITE_FILTERS and parsed.filter is None:
        parser.error("a custom --suite requires --filter")
    return parsed


def non_negative_integer(value: str) -> int:
    parsed = int(value)
    if parsed < 0:
        raise argparse.ArgumentTypeError("must be zero or greater")
    return parsed


def positive_integer(value: str) -> int:
    parsed = int(value)
    if parsed <= 0:
        raise argparse.ArgumentTypeError("must be greater than zero")
    return parsed


def resolve_filter(suite: str, filter_override: str | None) -> str | None:
    if filter_override is not None:
        return filter_override
    return SUITE_FILTERS.get(suite)


def build_test_command(
    dotnet: str,
    target: str,
    configuration: str,
    results_directory: Path,
    test_filter: str | None,
) -> list[str]:
    command = [
        dotnet,
        "test",
        target,
        "--configuration",
        configuration,
        "--no-build",
        "--logger",
        "trx",
        "--results-directory",
        str(results_directory),
    ]
    if test_filter is not None:
        command.extend(("--filter", test_filter))
    return command


def parse_trx(path: Path) -> TestCounts:
    root = ET.parse(path).getroot()
    counters = next(
        (element for element in root.iter() if local_name(element.tag) == "Counters"),
        None,
    )
    if counters is None:
        raise ValueError(f"TRX contains no Counters element: {path}")

    discovered = counter_value(counters, "total")
    passed = counter_value(counters, "passed")
    failed = counter_value(counters, "failed")
    executed = counter_value(counters, "executed", default=passed + failed)
    skipped = counter_value(
        counters,
        "notExecuted",
        default=max(0, discovered - executed),
    )
    return TestCounts(discovered, passed, failed, skipped)


def aggregate_trx(paths: Sequence[Path]) -> TestCounts:
    if not paths:
        raise ValueError("dotnet test produced no TRX files")
    total = TestCounts(0, 0, 0, 0)
    for path in paths:
        total += parse_trx(path)
    return total


def local_name(name: str) -> str:
    return name.rsplit("}", 1)[-1]


def counter_value(
    counters: ET.Element,
    name: str,
    default: int | None = None,
) -> int:
    value = counters.get(name)
    if value is None:
        if default is None:
            raise ValueError(f"TRX Counters element has no {name!r} attribute")
        return default
    return int(value)


def git_output(repository_root: Path, *arguments: str) -> str:
    result = subprocess.run(
        ("git", *arguments),
        cwd=repository_root,
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def read_cpu_description() -> str:
    description = platform.processor().strip()
    if description:
        return description
    if os.name == "nt":
        return os.environ.get("PROCESSOR_IDENTIFIER", "unknown")
    cpu_info = Path("/proc/cpuinfo")
    if cpu_info.exists():
        for line in cpu_info.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.lower().startswith("model name") and ":" in line:
                return line.split(":", 1)[1].strip()
    return "unknown"


def run_trial(
    *,
    kind: str,
    index: int,
    command: Sequence[str],
    results_directory: Path,
    repository_root: Path,
) -> dict[str, object]:
    results_directory.mkdir(parents=True, exist_ok=False)
    started_at = datetime.now(timezone.utc)
    started = time.perf_counter()
    completed = subprocess.run(command, cwd=repository_root, check=False)
    wall_time = time.perf_counter() - started
    trx_files = sorted(results_directory.rglob("*.trx"))

    counts: TestCounts | None = None
    parse_error: str | None = None
    try:
        counts = aggregate_trx(trx_files)
    except (ET.ParseError, OSError, ValueError) as error:
        parse_error = str(error)

    return {
        "kind": kind,
        "index": index,
        "started_at_utc": started_at.isoformat(),
        "wall_time_seconds": round(wall_time, 6),
        "exit_code": completed.returncode,
        "counts": asdict(counts) if counts is not None else None,
        "trx_files": [
            display_path(path, repository_root) for path in trx_files
        ],
        "parse_error": parse_error,
    }


def trial_succeeded(trial: dict[str, object]) -> bool:
    return trial["exit_code"] == 0 and trial["parse_error"] is None


def consistent_sample_counts(samples: Sequence[dict[str, object]]) -> object:
    counts = [sample["counts"] for sample in samples]
    if not counts or any(value is None for value in counts):
        return None
    first = counts[0]
    return first if all(value == first for value in counts) else None


def safe_name(value: str) -> str:
    normalized = "".join(
        character.lower() if character.isalnum() else "-" for character in value
    )
    return "-".join(part for part in normalized.split("-") if part) or "suite"


def display_path(path: Path, repository_root: Path) -> str:
    try:
        return str(path.relative_to(repository_root))
    except ValueError:
        return str(path)


def main(arguments: Sequence[str] | None = None) -> int:
    options = parse_arguments(arguments if arguments is not None else sys.argv[1:])
    repository_root = Path(__file__).resolve().parents[2]
    commit = git_output(repository_root, "rev-parse", "HEAD")
    dirty = bool(git_output(repository_root, "status", "--short"))
    dotnet_version = subprocess.run(
        (options.dotnet, "--version"),
        cwd=repository_root,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()

    output_root = Path(options.output_directory)
    if not output_root.is_absolute():
        output_root = repository_root / output_root
    output_root.mkdir(parents=True, exist_ok=True)

    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    run_name = f"{options.stage}-{safe_name(options.suite)}-{timestamp}"
    run_directory = output_root / run_name
    run_directory.mkdir(parents=True, exist_ok=False)
    test_filter = resolve_filter(options.suite, options.filter)
    command_template = build_test_command(
        options.dotnet,
        options.target,
        options.configuration,
        Path("<results-directory>"),
        test_filter,
    )

    result: dict[str, object] = {
        "schema_version": SCHEMA_VERSION,
        "stage": options.stage,
        "suite": options.suite,
        "measured_at_utc": datetime.now(timezone.utc).isoformat(),
        "repository": {"commit": commit, "dirty": dirty},
        "environment": {
            "os": platform.platform(),
            "architecture": platform.machine(),
            "cpu": read_cpu_description(),
            "dotnet_sdk": dotnet_version,
        },
        "test": {
            "target": options.target,
            "configuration": options.configuration,
            "filter": test_filter,
            "command": command_template,
        },
        "iterations": {
            "warm_up": options.warm_up,
            "samples": options.repeat,
        },
        "warm_ups": [],
        "samples": [],
        "summary": None,
    }
    json_path = run_directory / "measurement.json"

    failed = False
    for kind, count in (("warm-up", options.warm_up), ("sample", options.repeat)):
        collection_name = "warm_ups" if kind == "warm-up" else "samples"
        collection = result[collection_name]
        assert isinstance(collection, list)
        for index in range(1, count + 1):
            trial_directory = run_directory / f"{kind}-{index:03d}"
            command = build_test_command(
                options.dotnet,
                options.target,
                options.configuration,
                trial_directory,
                test_filter,
            )
            print(f"Running {kind} {index}/{count}: {' '.join(command)}", flush=True)
            trial = run_trial(
                kind=kind,
                index=index,
                command=command,
                results_directory=trial_directory,
                repository_root=repository_root,
            )
            collection.append(trial)
            if not trial_succeeded(trial):
                failed = True
                break
        if failed:
            break

    samples = result["samples"]
    assert isinstance(samples, list)
    sample_counts = consistent_sample_counts(samples)
    successful = (
        not failed
        and len(samples) == options.repeat
        and all(trial_succeeded(sample) for sample in samples)
        and isinstance(sample_counts, dict)
        and int(sample_counts["discovered"]) > 0
    )
    wall_times = [float(sample["wall_time_seconds"]) for sample in samples]
    result["summary"] = {
        "successful": successful,
        "test_counts": sample_counts,
        "wall_time_seconds": {
            "samples": wall_times,
            "median": round(statistics.median(wall_times), 6) if wall_times else None,
        },
    }
    json_path.write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Measurement JSON: {display_path(json_path, repository_root)}")
    if not successful:
        print("Measurement did not complete successfully.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
