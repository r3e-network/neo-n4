#!/usr/bin/env python3
"""Run a filtered dotnet test gate and reject empty, partial, or skipped selections."""

from __future__ import annotations

import argparse
import shlex
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence


@dataclass(frozen=True)
class TestCounters:
    total: int
    executed: int
    passed: int
    failed: int
    not_executed: int

    def __add__(self, other: "TestCounters") -> "TestCounters":
        return TestCounters(
            total=self.total + other.total,
            executed=self.executed + other.executed,
            passed=self.passed + other.passed,
            failed=self.failed + other.failed,
            not_executed=self.not_executed + other.not_executed,
        )


def parse_trx_counters(path: Path) -> TestCounters:
    root = ElementTree.parse(path).getroot()
    counter_elements = [
        element for element in root.iter() if element.tag.rsplit("}", 1)[-1] == "Counters"
    ]
    if len(counter_elements) != 1:
        raise ValueError(
            f"expected exactly one TRX Counters element in {path}, found {len(counter_elements)}"
        )

    attributes = counter_elements[0].attrib

    def read(name: str) -> int:
        value = attributes.get(name)
        if value is None:
            raise ValueError(f"TRX Counters in {path} is missing {name!r}")
        return int(value)

    return TestCounters(
        total=read("total"),
        executed=read("executed"),
        passed=read("passed"),
        failed=read("failed"),
        not_executed=read("notExecuted"),
    )


def aggregate_trx_counters(paths: Sequence[Path]) -> TestCounters:
    if not paths:
        raise ValueError("dotnet test did not produce a TRX result")

    total = TestCounters(0, 0, 0, 0, 0)
    for path in paths:
        total += parse_trx_counters(path)
    return total


def validation_errors(counters: TestCounters, expected_tests: int) -> list[str]:
    errors: list[str] = []
    if counters.total != expected_tests:
        errors.append(f"expected {expected_tests} selected tests, found {counters.total}")
    if counters.executed != expected_tests:
        errors.append(f"expected {expected_tests} executed tests, found {counters.executed}")
    if counters.passed != expected_tests:
        errors.append(f"expected {expected_tests} passed tests, found {counters.passed}")
    if counters.failed != 0:
        errors.append(f"expected zero failed tests, found {counters.failed}")
    if counters.not_executed != 0:
        errors.append(f"expected zero skipped tests, found {counters.not_executed}")
    return errors


def positive_integer(value: str) -> int:
    parsed = int(value)
    if parsed < 1:
        raise argparse.ArgumentTypeError("expected-tests must be at least 1")
    return parsed


def parse_arguments(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--filter", required=True, dest="test_filter")
    parser.add_argument("--expected-tests", required=True, type=positive_integer)
    parser.add_argument("dotnet_arguments", nargs=argparse.REMAINDER)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    arguments = parse_arguments(sys.argv[1:] if argv is None else argv)
    extra_arguments = list(arguments.dotnet_arguments)
    if extra_arguments[:1] == ["--"]:
        extra_arguments = extra_arguments[1:]

    with tempfile.TemporaryDirectory(prefix="neo-n4-dotnet-filter-") as temporary_directory:
        results_directory = Path(temporary_directory)
        command = [
            "dotnet",
            "test",
            str(arguments.project),
            "--filter",
            arguments.test_filter,
            *extra_arguments,
            "--logger",
            "trx;LogFileName=filtered-tests.trx",
            "--results-directory",
            str(results_directory),
        ]
        print(f"+ {shlex.join(command)}", flush=True)
        completed = subprocess.run(command, check=False)
        if completed.returncode != 0:
            return completed.returncode

        try:
            counters = aggregate_trx_counters(sorted(results_directory.rglob("*.trx")))
        except (ElementTree.ParseError, OSError, ValueError) as error:
            print(f"ERROR: {error}", file=sys.stderr)
            return 1

        errors = validation_errors(counters, arguments.expected_tests)
        if errors:
            for error in errors:
                print(f"ERROR: {error}", file=sys.stderr)
            return 1

        print(
            f"Filtered test gate passed: {counters.passed}/{arguments.expected_tests} tests passed; none skipped."
        )
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
