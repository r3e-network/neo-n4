#!/usr/bin/env python3
from __future__ import annotations

import argparse
import importlib.util
import json
import sys
import unittest
from pathlib import Path


TESTS_DIRECTORY = Path(__file__).resolve().parent
SDK_DIRECTORY = TESTS_DIRECTORY.parent


def load_module(mode: str):
    path = TESTS_DIRECTORY / f"test_conformance_{mode}.py"
    specification = importlib.util.spec_from_file_location(f"sdk_conformance_{mode}", path)
    if specification is None or specification.loader is None:
        raise RuntimeError(f"cannot load {path}")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("offline", "live"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()

    sys.path.insert(0, str(SDK_DIRECTORY))
    module = load_module(arguments.mode)
    suite = unittest.defaultTestLoader.loadTestsFromModule(module)
    discovered = suite.countTestCases()
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    skipped = len(result.skipped)
    failed = len(result.failures) + len(result.errors) + len(result.unexpectedSuccesses)
    executed = result.testsRun - skipped
    passed = executed - failed
    summary = {
        "language": "python",
        "discovered": discovered,
        "executed": executed,
        "passed": passed,
        "failed": failed,
        "skipped": skipped,
    }
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
