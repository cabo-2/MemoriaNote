from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "measure-phase-7-tests.py"
SPEC = importlib.util.spec_from_file_location("measure_phase_7_tests", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load measurement script: {SCRIPT_PATH}")
MEASURE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MEASURE
SPEC.loader.exec_module(MEASURE)


class TrxParsingTests(unittest.TestCase):
    def test_parse_trx_reads_namespaced_counters(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            trx = Path(directory) / "result.trx"
            trx.write_text(
                """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <ResultSummary outcome="Completed">
    <Counters total="12" executed="11" passed="9" failed="2"
              notExecuted="1" />
  </ResultSummary>
</TestRun>
""",
                encoding="utf-8",
            )

            counts = MEASURE.parse_trx(trx)

            self.assertEqual(
                counts,
                MEASURE.TestCounts(discovered=12, passed=9, failed=2, skipped=1),
            )

    def test_aggregate_trx_combines_test_projects(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            paths = []
            for name, total, passed, failed in (
                ("core.trx", 404, 404, 0),
                ("cli.trx", 395, 394, 1),
            ):
                path = Path(directory) / name
                path.write_text(
                    f"""<TestRun><ResultSummary><Counters total="{total}"
executed="{total}" passed="{passed}" failed="{failed}" notExecuted="0" />
</ResultSummary></TestRun>""",
                    encoding="utf-8",
                )
                paths.append(path)

            counts = MEASURE.aggregate_trx(paths)

            self.assertEqual(
                counts,
                MEASURE.TestCounts(discovered=799, passed=798, failed=1, skipped=0),
            )


class CommandConstructionTests(unittest.TestCase):
    def test_fast_suite_uses_process_exclusion_filter(self) -> None:
        test_filter = MEASURE.resolve_filter("fast", None)
        command = MEASURE.build_test_command(
            "dotnet",
            "MemoriaNote.sln",
            "Release",
            Path("results"),
            test_filter,
        )

        self.assertEqual(test_filter, "TestCategory!=Process")
        self.assertEqual(command[-2:], ["--filter", "TestCategory!=Process"])
        self.assertIn("--no-build", command)

    def test_full_suite_has_no_filter(self) -> None:
        command = MEASURE.build_test_command(
            "dotnet",
            "MemoriaNote.sln",
            "Release",
            Path("results"),
            MEASURE.resolve_filter("full", None),
        )

        self.assertNotIn("--filter", command)


if __name__ == "__main__":
    unittest.main()
