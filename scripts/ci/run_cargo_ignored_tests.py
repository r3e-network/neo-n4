#!/usr/bin/env python3
"""Discover and run an exact set of ignored Rust integration tests."""

from __future__ import annotations

import argparse
import re
import shlex
import subprocess
import sys
from pathlib import Path
from typing import Sequence


TEST_LINE = re.compile(r"^(.+): test$")


def parse_listed_tests(output: str) -> set[str]:
    tests: set[str] = set()
    for line in output.splitlines():
        match = TEST_LINE.fullmatch(line.strip())
        if match:
            tests.add(match.group(1))
    return tests


def selection_errors(discovered: set[str], expected: set[str]) -> list[str]:
    errors: list[str] = []
    missing = sorted(expected - discovered)
    unexpected = sorted(discovered - expected)
    if missing:
        errors.append(f"missing ignored tests: {', '.join(missing)}")
    if unexpected:
        errors.append(f"unexpected ignored tests: {', '.join(unexpected)}")
    if not discovered:
        errors.append("ignored-test discovery selected zero tests")
    return errors


def parse_arguments(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest-path", required=True, type=Path)
    parser.add_argument("--test-target", required=True)
    parser.add_argument("--expected-test", required=True, action="append", dest="expected_tests")
    parser.add_argument("--release", action="store_true")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    arguments = parse_arguments(sys.argv[1:] if argv is None else argv)
    expected_tests = set(arguments.expected_tests)
    if len(expected_tests) != len(arguments.expected_tests):
        print("ERROR: expected-test values must be unique", file=sys.stderr)
        return 2

    command = [
        "cargo",
        "test",
        "--manifest-path",
        str(arguments.manifest_path),
        "--locked",
        "--test",
        arguments.test_target,
    ]
    if arguments.release:
        command.append("--release")

    discovery_command = [*command, "--", "--ignored", "--list"]
    print(f"+ {shlex.join(discovery_command)}", flush=True)
    discovery = subprocess.run(discovery_command, check=False, capture_output=True, text=True)
    sys.stdout.write(discovery.stdout)
    sys.stderr.write(discovery.stderr)
    if discovery.returncode != 0:
        return discovery.returncode

    errors = selection_errors(parse_listed_tests(discovery.stdout), expected_tests)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    test_command = [*command, "--", "--ignored", "--nocapture"]
    print(f"+ {shlex.join(test_command)}", flush=True)
    return subprocess.run(test_command, check=False).returncode


if __name__ == "__main__":
    raise SystemExit(main())
