from __future__ import annotations

import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


RUNNER_PATH = Path(__file__).resolve().parents[1] / "run_sdk_conformance.py"
SPECIFICATION = importlib.util.spec_from_file_location("run_sdk_conformance", RUNNER_PATH)
if SPECIFICATION is None or SPECIFICATION.loader is None:
    raise RuntimeError(f"cannot load {RUNNER_PATH}")
runner = importlib.util.module_from_spec(SPECIFICATION)
sys.modules[SPECIFICATION.name] = runner
SPECIFICATION.loader.exec_module(runner)


class SdkConformanceRunnerTests(unittest.TestCase):
    def summary(self, **overrides):
        values = {
            "language": "rust",
            "discovered": 3,
            "executed": 3,
            "passed": 3,
            "failed": 0,
            "skipped": 0,
            "return_code": 0,
            "duration_seconds": 0.1,
            "command": ["cargo", "test"],
        }
        values.update(overrides)
        return runner.SuiteSummary(**values)

    def test_zero_discovered_tests_fail_closed(self) -> None:
        errors = runner.validation_errors(
            self.summary(discovered=0, executed=0, passed=0),
            "offline",
            True,
        )
        self.assertTrue(any("zero tests" in error for error in errors))

    def test_inconsistent_counters_fail_closed(self) -> None:
        errors = runner.validation_errors(
            self.summary(discovered=3, executed=2, passed=2, skipped=0),
            "offline",
            False,
        )
        self.assertTrue(any("inconsistent counters" in error for error in errors))

    def test_live_configuration_requires_real_execution(self) -> None:
        errors = runner.validation_errors(
            self.summary(executed=0, passed=0, skipped=3),
            "live",
            True,
        )
        self.assertTrue(any("executed tests" in error for error in errors))

    def test_missing_live_configuration_requires_explicit_skip(self) -> None:
        self.assertEqual(
            [],
            runner.validation_errors(
                self.summary(executed=0, passed=0, skipped=3),
                "live",
                False,
            ),
        )

    def test_missing_live_configuration_rejects_false_success(self) -> None:
        errors = runner.validation_errors(self.summary(), "live", False)
        self.assertTrue(any("executed without" in error for error in errors))

    def test_rust_parser_counts_ignored_tests_as_discovered(self) -> None:
        output = "test result: ok. 0 passed; 0 failed; 3 ignored; 0 measured; 0 filtered out"
        self.assertEqual((3, 0, 0, 0, 3), runner.parse_rust_result(output))

    def test_trx_parser_reports_selected_and_skipped_tests(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "result.trx"
            path.write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                '<ResultSummary><Counters total="3" executed="0" passed="0" failed="0" '
                'notExecuted="3" /></ResultSummary></TestRun>',
                encoding="utf-8",
            )
            self.assertEqual((3, 0, 0, 0, 3), runner.parse_trx(path))

    def test_checked_in_canonical_vectors_pass_schema_validation(self) -> None:
        self.assertEqual([], runner.validate_canonical_vectors())

    def test_live_configuration_requires_explicit_switch_and_fixture(self) -> None:
        missing, errors, requested = runner.live_configuration({})
        self.assertFalse(requested)
        self.assertEqual([], errors)
        self.assertIn("NEO_SDK_LIVE", missing)
        self.assertIn("NEO_SDK_LIVE_FIXTURE", missing)

    def test_live_configuration_rejects_noncanonical_switch(self) -> None:
        fixture = runner.REPOSITORY_ROOT / "sdk" / "conformance" / "live-fixture.example.json"
        environment = {
            "NEO_SDK_LIVE": "true",
            "NEO_N3_RPC_URL": "https://n3.example",
            "NEO_N4_RPC_URL": "https://n4.example",
            "NEO_N4_CHAIN_ID": "1099",
            "NEO_SDK_LIVE_FIXTURE": str(fixture),
        }
        missing, errors, requested = runner.live_configuration(environment)
        self.assertTrue(requested)
        self.assertEqual([], missing)
        self.assertTrue(any("must equal 1" in error for error in errors))

    def test_example_live_fixture_passes_schema_validation(self) -> None:
        fixture = runner.REPOSITORY_ROOT / "sdk" / "conformance" / "live-fixture.example.json"
        self.assertEqual([], runner.validate_live_fixture(fixture, 1099))

    def test_live_fixture_chain_mismatch_fails_closed(self) -> None:
        fixture = runner.REPOSITORY_ROOT / "sdk" / "conformance" / "live-fixture.example.json"
        errors = runner.validate_live_fixture(fixture, 1101)
        self.assertTrue(any("does not match NEO_N4_CHAIN_ID" in error for error in errors))

    def test_live_fixture_rejects_noncanonical_batch_encoding(self) -> None:
        fixture = runner.REPOSITORY_ROOT / "sdk" / "conformance" / "live-fixture.example.json"
        value = json.loads(fixture.read_text(encoding="utf-8"))
        value["n4"]["cases"][0]["result"]["encoded"] = "00"
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "fixture.json"
            path.write_text(json.dumps(value), encoding="utf-8")
            errors = runner.validate_live_fixture(path, 1099)
        self.assertTrue(any("canonical structured fields" in error for error in errors))

    def test_malformed_live_fixture_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "fixture.json"
            path.write_text("{}\n", encoding="utf-8")
            errors = runner.validate_live_fixture(path, 1099)
        self.assertTrue(any("schema" in error for error in errors))
        self.assertTrue(any(error.startswith("n4") for error in errors))

    def test_missing_executable_returns_failed_completed_process(self) -> None:
        with patch.object(runner.subprocess, "run", side_effect=FileNotFoundError("missing")):
            completed, _ = runner.run(
                ["missing-sdk-runner"],
                cwd=runner.REPOSITORY_ROOT,
                environment=os.environ,
            )
        self.assertEqual(127, completed.returncode)
        self.assertIn("cannot execute", completed.stdout)


if __name__ == "__main__":
    unittest.main()
