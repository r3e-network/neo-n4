from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


CI_ROOT = Path(__file__).resolve().parents[1]


def load_module(name: str):
    specification = importlib.util.spec_from_file_location(name, CI_ROOT / f"{name}.py")
    if specification is None or specification.loader is None:
        raise RuntimeError(f"cannot load {name}")
    module = importlib.util.module_from_spec(specification)
    sys.modules[name] = module
    specification.loader.exec_module(module)
    return module


dotnet_gate = load_module("run_dotnet_filtered_tests")
cargo_gate = load_module("run_cargo_ignored_tests")


class DotnetFilteredTestGateTests(unittest.TestCase):
    def test_trx_requires_every_expected_test_to_pass(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            trx = Path(temporary_directory) / "results.trx"
            trx.write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                '<ResultSummary><Counters total="3" executed="3" passed="3" failed="0" '
                'notExecuted="0" /></ResultSummary></TestRun>',
                encoding="utf-8",
            )
            counters = dotnet_gate.aggregate_trx_counters([trx])

        self.assertEqual([], dotnet_gate.validation_errors(counters, 3))

    def test_zero_selected_tests_fail_closed(self) -> None:
        counters = dotnet_gate.TestCounters(0, 0, 0, 0, 0)

        errors = dotnet_gate.validation_errors(counters, 1)

        self.assertTrue(any("selected tests" in error for error in errors))

    def test_selection_below_minimum_fails_closed(self) -> None:
        counters = dotnet_gate.TestCounters(2, 2, 2, 0, 0)

        errors = dotnet_gate.validation_errors(counters, 3)

        self.assertTrue(any("at least 3 selected tests" in error for error in errors))

    def test_selection_above_minimum_remains_valid(self) -> None:
        counters = dotnet_gate.TestCounters(4, 4, 4, 0, 0)

        errors = dotnet_gate.validation_errors(counters, 3)

        self.assertEqual([], errors)

    def test_skipped_test_fails_closed(self) -> None:
        counters = dotnet_gate.TestCounters(3, 2, 2, 0, 1)

        errors = dotnet_gate.validation_errors(counters, 3)

        self.assertTrue(any("skipped tests" in error for error in errors))

    def test_missing_trx_fails_closed(self) -> None:
        with self.assertRaisesRegex(ValueError, "did not produce a TRX result"):
            dotnet_gate.aggregate_trx_counters([])

    def test_malformed_trx_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            trx = Path(temporary_directory) / "results.trx"
            trx.write_text("<TestRun>", encoding="utf-8")

            with self.assertRaises(dotnet_gate.ElementTree.ParseError):
                dotnet_gate.parse_trx_counters(trx)

    def test_missing_counters_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            trx = Path(temporary_directory) / "results.trx"
            trx.write_text("<TestRun><ResultSummary /></TestRun>", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "exactly one TRX Counters"):
                dotnet_gate.parse_trx_counters(trx)

    def test_inconsistent_execution_count_fails_closed(self) -> None:
        counters = dotnet_gate.TestCounters(3, 2, 3, 0, 0)

        errors = dotnet_gate.validation_errors(counters, 3)

        self.assertTrue(any("selected tests to execute" in error for error in errors))


class CargoIgnoredTestGateTests(unittest.TestCase):
    def test_exact_ignored_test_selection_passes(self) -> None:
        output = "prove: test\nrejects_tamper: test\n\n2 tests, 0 benchmarks\n"

        discovered = cargo_gate.parse_listed_tests(output)

        self.assertEqual([], cargo_gate.selection_errors(discovered, {"prove", "rejects_tamper"}))

    def test_empty_ignored_test_selection_fails_closed(self) -> None:
        errors = cargo_gate.selection_errors(set(), {"prove"})

        self.assertTrue(any("zero tests" in error for error in errors))

    def test_unexpected_ignored_test_requires_gate_update(self) -> None:
        errors = cargo_gate.selection_errors({"prove", "new_gate"}, {"prove"})

        self.assertTrue(any("unexpected ignored tests" in error for error in errors))

    def test_missing_ignored_test_fails_closed(self) -> None:
        errors = cargo_gate.selection_errors({"prove"}, {"prove", "rejects_tamper"})

        self.assertTrue(any("missing ignored tests" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
