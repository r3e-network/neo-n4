#!/usr/bin/env python3
"""Run the shared four-language SDK conformance suites and emit a JSON summary."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shlex
import struct
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ElementTree
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Mapping, Sequence


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VECTOR_PATH = REPOSITORY_ROOT / "sdk" / "conformance" / "vectors" / "v1.json"
LIVE_ENABLE_VARIABLE = "NEO_SDK_LIVE"
LIVE_FIXTURE_VARIABLE = "NEO_SDK_LIVE_FIXTURE"
REQUIRED_LIVE_VARIABLES = (
    LIVE_ENABLE_VARIABLE,
    "NEO_N3_RPC_URL",
    "NEO_N4_RPC_URL",
    "NEO_N4_CHAIN_ID",
    LIVE_FIXTURE_VARIABLE,
)
LANGUAGES = ("dotnet", "rust", "typescript", "python")
EXPECTED_VECTOR_CASES = {
    "batch-missing-max-u64": "getl2batch",
    "batch-complete-large-u64": "getl2batch",
    "batch-status": "getl2batchstatus",
    "latest-state-root": "getl2stateroot",
    "state-root-max-u64": "getl2stateroot",
    "withdrawal-proof": "getl2withdrawalproof",
    "message-proof": "getl2messageproof",
    "deposit-status-max-u64": "getl1depositstatus",
    "canonical-asset": "getcanonicalasset",
    "bridged-asset": "getbridgedasset",
    "security-level": "getsecuritylevel",
    "security-label": "getsecuritylabel",
}
EXPECTED_RESPONSE_ERRORS = {
    "mismatched-chain-id": "getsecuritylabel",
    "invalid-withdrawal-proof-hex": "getl2withdrawalproof",
    "wrong-state-root-type": "getl2stateroot",
    "unsafe-numeric-u64": "getl1depositstatus",
}
EXPECTED_ENVELOPE_ERRORS = {
    "server": "server",
    "mismatched-id": "protocol",
    "wrong-jsonrpc-version": "protocol",
}
EXPECTED_LIVE_CASES = {
    "batch": "getl2batch",
    "batch-status": "getl2batchstatus",
    "latest-state-root": "getl2stateroot",
    "historical-state-root": "getl2stateroot",
    "withdrawal-proof": "getl2withdrawalproof",
    "message-proof": "getl2messageproof",
    "deposit-status": "getl1depositstatus",
    "canonical-asset": "getcanonicalasset",
    "bridged-asset": "getbridgedasset",
    "security-level": "getsecuritylevel",
    "security-label": "getsecuritylabel",
}
NON_NULL_LIVE_CASES = {
    "batch",
    "withdrawal-proof",
    "message-proof",
    "deposit-status",
    "canonical-asset",
    "bridged-asset",
}
RUST_RESULT = re.compile(
    r"test result: (?:ok|FAILED)\. (?P<passed>\d+) passed; (?P<failed>\d+) failed; "
    r"(?P<ignored>\d+) ignored;"
)


@dataclass(frozen=True)
class SuiteSummary:
    language: str
    discovered: int
    executed: int
    passed: int
    failed: int
    skipped: int
    return_code: int
    duration_seconds: float
    command: list[str]


def load_json_object(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except OSError as error:
        raise ValueError(f"cannot read {label} {path}: {error}") from error
    except json.JSONDecodeError as error:
        raise ValueError(f"cannot parse {label} {path}: {error}") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} {path} must contain a JSON object")
    return value


def validate_named_cases(
    cases: Any,
    expected: Mapping[str, str],
    label: str,
) -> list[str]:
    if not isinstance(cases, list):
        return [f"{label} must be an array"]
    found: dict[str, str] = {}
    errors: list[str] = []
    for index, case in enumerate(cases):
        if not isinstance(case, dict):
            errors.append(f"{label}[{index}] must be an object")
            continue
        name = case.get("name")
        method = case.get("method")
        if not isinstance(name, str) or not name:
            errors.append(f"{label}[{index}].name must be a non-empty string")
            continue
        if name in found:
            errors.append(f"{label} contains duplicate case {name}")
            continue
        if not isinstance(method, str) or not method:
            errors.append(f"{label}[{index}].method must be a non-empty string")
            continue
        found[name] = method
    if found != dict(expected):
        errors.append(f"{label} case map differs: expected {dict(expected)}, found {found}")
    return errors


def validate_hash(value: Any, label: str) -> list[str]:
    if not isinstance(value, str) or re.fullmatch(r"0x[0-9a-fA-F]{64}", value) is None:
        return [f"{label} must be a 0x-prefixed 32-byte hash"]
    return []


def validate_error_cases(cases: Any) -> list[str]:
    if not isinstance(cases, list):
        return ["rpc.errors must be an array"]
    found: dict[str, str] = {}
    errors: list[str] = []
    for index, case in enumerate(cases):
        if not isinstance(case, dict):
            errors.append(f"rpc.errors[{index}] must be an object")
            continue
        name = case.get("name")
        expected = case.get("expected")
        if not isinstance(name, str) or not name:
            errors.append(f"rpc.errors[{index}].name must be a non-empty string")
            continue
        if name in found:
            errors.append(f"rpc.errors contains duplicate case {name}")
            continue
        if not isinstance(expected, str) or not expected:
            errors.append(f"rpc.errors[{index}].expected must be a non-empty string")
            continue
        found[name] = expected
    if found != EXPECTED_ENVELOPE_ERRORS:
        errors.append(
            f"rpc.errors case map differs: expected {EXPECTED_ENVELOPE_ERRORS}, found {found}"
        )
    return errors


def parse_hex(value: Any, label: str, *, expected_bytes: int | None = None) -> tuple[bytes | None, list[str]]:
    if not isinstance(value, str):
        return None, [f"{label} must be a hex string"]
    text = value[2:] if value.lower().startswith("0x") else value
    if len(text) % 2 != 0 or re.fullmatch(r"[0-9a-fA-F]*", text) is None:
        return None, [f"{label} must contain an even number of hexadecimal characters"]
    try:
        decoded = bytes.fromhex(text)
    except ValueError as error:
        return None, [f"{label} is invalid hex: {error}"]
    if expected_bytes is not None and len(decoded) != expected_bytes:
        return None, [f"{label} must contain exactly {expected_bytes} bytes"]
    return decoded, []


def parse_fixture_u64(value: Any, label: str) -> tuple[int | None, list[str]]:
    if isinstance(value, bool):
        return None, [f"{label} must be a lossless u64"]
    if isinstance(value, int):
        parsed = value
    elif isinstance(value, str) and re.fullmatch(r"0|[1-9][0-9]*", value):
        parsed = int(value, 10)
    else:
        return None, [f"{label} must be a canonical decimal string or JSON integer"]
    if not 0 <= parsed <= 0xFFFF_FFFF_FFFF_FFFF:
        return None, [f"{label} exceeds u64 range"]
    return parsed, []


def validate_live_batch(case: Mapping[str, Any], expected_chain_id: int) -> list[str]:
    result = case.get("result")
    if not isinstance(result, dict):
        return ["live fixture batch result must be an object"]
    errors: list[str] = []
    if result.get("chainId") != expected_chain_id:
        errors.append("live fixture batch result.chainId must match n4.chainId")

    integer_values: list[int] = []
    for field in ("batchNumber", "firstBlock", "lastBlock"):
        parsed, field_errors = parse_fixture_u64(result.get(field), f"live fixture batch result.{field}")
        errors.extend(field_errors)
        if parsed is not None:
            integer_values.append(parsed)
    if len(integer_values) == 3 and integer_values[2] < integer_values[1]:
        errors.append("live fixture batch result.lastBlock must not precede firstBlock")

    root_bytes: list[bytes] = []
    for field in (
        "preStateRoot",
        "postStateRoot",
        "txRoot",
        "receiptRoot",
        "withdrawalRoot",
        "l2ToL1MessageRoot",
        "l2ToL2MessageRoot",
        "daCommitment",
        "publicInputHash",
    ):
        root, field_errors = parse_hex(
            result.get(field),
            f"live fixture batch result.{field}",
            expected_bytes=32,
        )
        errors.extend(field_errors)
        if root is not None:
            root_bytes.append(root[::-1])

    proof_type = result.get("proofType")
    if isinstance(proof_type, bool) or not isinstance(proof_type, int) or not 0 <= proof_type <= 3:
        errors.append("live fixture batch result.proofType must be an integer in 0..3")
    proof, proof_errors = parse_hex(result.get("proof"), "live fixture batch result.proof")
    errors.extend(proof_errors)
    if proof == b"":
        errors.append("live fixture batch result.proof must be non-empty")
    encoded, encoded_errors = parse_hex(result.get("encoded"), "live fixture batch result.encoded")
    errors.extend(encoded_errors)

    params = case.get("params")
    if isinstance(params, list) and len(params) == 2:
        requested_batch, requested_errors = parse_fixture_u64(
            params[1], "live fixture batch params[1]"
        )
        errors.extend(requested_errors)
        if integer_values and requested_batch is not None and requested_batch != integer_values[0]:
            errors.append("live fixture batch result.batchNumber must match params[1]")
    else:
        errors.append("live fixture batch params must contain chainId and batchNumber")

    if (
        not errors
        and len(integer_values) == 3
        and len(root_bytes) == 9
        and isinstance(proof_type, int)
        and proof is not None
        and encoded is not None
    ):
        canonical = bytearray(struct.pack("<IQQQ", expected_chain_id, *integer_values))
        for root in root_bytes:
            canonical.extend(root)
        canonical.extend(struct.pack("<BI", proof_type, len(proof)))
        canonical.extend(proof)
        if bytes(canonical) != encoded:
            errors.append(
                "live fixture batch result.encoded does not match its canonical structured fields"
            )
    return errors


def validate_canonical_vectors(path: Path = VECTOR_PATH) -> list[str]:
    try:
        vectors = load_json_object(path, "canonical vector")
    except ValueError as error:
        return [str(error)]
    errors: list[str] = []
    if vectors.get("schema") != "neo-n4-sdk-conformance/v1":
        errors.append("canonical vector schema must be neo-n4-sdk-conformance/v1")
    rpc = vectors.get("rpc")
    if not isinstance(rpc, dict):
        return [*errors, "canonical vector rpc must be an object"]
    errors.extend(validate_named_cases(rpc.get("cases"), EXPECTED_VECTOR_CASES, "rpc.cases"))
    errors.extend(validate_error_cases(rpc.get("errors")))
    errors.extend(
        validate_named_cases(
            rpc.get("responseErrors"),
            EXPECTED_RESPONSE_ERRORS,
            "rpc.responseErrors",
        )
    )
    cases = rpc.get("cases")
    if isinstance(cases, list):
        complete_batch = next(
            (
                case
                for case in cases
                if isinstance(case, dict) and case.get("name") == "batch-complete-large-u64"
            ),
            None,
        )
        if complete_batch is not None and isinstance(rpc.get("chainId"), int):
            errors.extend(validate_live_batch(complete_batch, rpc["chainId"]))
    domain = vectors.get("domain")
    if not isinstance(domain, dict):
        errors.append("canonical vector domain must be an object")
    else:
        if domain.get("l1ReservedChainId") != 0:
            errors.append("domain.l1ReservedChainId must be 0")
        if domain.get("l2ChainId") != rpc.get("chainId"):
            errors.append("domain.l2ChainId must match rpc.chainId")
        transaction = vectors.get("transaction")
        if not isinstance(transaction, dict) or domain.get("networkMagic") != transaction.get("network"):
            errors.append("domain.networkMagic must match transaction.network")
    return errors


def validate_node_fixture(node: Any, label: str) -> list[str]:
    if not isinstance(node, dict):
        return [f"{label} must be an object"]
    errors: list[str] = []
    network = node.get("networkMagic")
    if isinstance(network, bool) or not isinstance(network, int) or not 0 < network <= 0xFFFF_FFFF:
        errors.append(f"{label}.networkMagic must be a non-zero u32")
    minimum = node.get("minimumBlockCount")
    if isinstance(minimum, bool) or not isinstance(minimum, int) or minimum < 1:
        errors.append(f"{label}.minimumBlockCount must be a positive integer")
    errors.extend(validate_hash(node.get("genesisHash"), f"{label}.genesisHash"))
    return errors


def validate_live_fixture(path: Path, expected_chain_id: int) -> list[str]:
    try:
        fixture = load_json_object(path, "live fixture")
    except ValueError as error:
        return [str(error)]
    errors: list[str] = []
    if fixture.get("schema") != "neo-n4-sdk-live-fixture/v1":
        errors.append("live fixture schema must be neo-n4-sdk-live-fixture/v1")
    errors.extend(validate_node_fixture(fixture.get("n3"), "n3"))
    n4 = fixture.get("n4")
    errors.extend(validate_node_fixture(n4, "n4"))
    if not isinstance(n4, dict):
        return errors
    if n4.get("chainId") != expected_chain_id:
        errors.append(
            f"live fixture n4.chainId {n4.get('chainId')} does not match NEO_N4_CHAIN_ID {expected_chain_id}"
        )
    wrong_chain_id = n4.get("wrongChainId")
    if (
        isinstance(wrong_chain_id, bool)
        or not isinstance(wrong_chain_id, int)
        or not 0 < wrong_chain_id <= 0xFFFF_FFFF
        or wrong_chain_id == expected_chain_id
    ):
        errors.append("live fixture n4.wrongChainId must be a distinct non-zero u32")
    cases = n4.get("cases")
    errors.extend(validate_named_cases(cases, EXPECTED_LIVE_CASES, "n4.cases"))
    if isinstance(cases, list):
        for case in cases:
            if isinstance(case, dict) and case.get("name") in NON_NULL_LIVE_CASES and case.get("result") is None:
                errors.append(f"live fixture case {case.get('name')} must contain non-null production evidence")
            if isinstance(case, dict) and case.get("name") == "batch":
                errors.extend(validate_live_batch(case, expected_chain_id))
            if isinstance(case, dict) and case.get("name") in {"withdrawal-proof", "message-proof"}:
                proof, proof_errors = parse_hex(
                    case.get("result"), f"live fixture case {case.get('name')} result"
                )
                errors.extend(proof_errors)
                if proof == b"":
                    errors.append(f"live fixture case {case.get('name')} result must be non-empty")
    return errors


def live_configuration(
    environment: Mapping[str, str],
) -> tuple[list[str], list[str], bool]:
    missing = [name for name in REQUIRED_LIVE_VARIABLES if not environment.get(name, "").strip()]
    requested = not missing
    errors: list[str] = []
    switch = environment.get(LIVE_ENABLE_VARIABLE, "").strip()
    if switch and switch != "1":
        errors.append(f"{LIVE_ENABLE_VARIABLE} must equal 1, got {switch!r}")
    chain_id_text = environment.get("NEO_N4_CHAIN_ID", "").strip()
    chain_id: int | None = None
    if chain_id_text:
        try:
            chain_id = int(chain_id_text, 10)
        except ValueError:
            errors.append("NEO_N4_CHAIN_ID must be a decimal non-zero u32")
        else:
            if not 0 < chain_id <= 0xFFFF_FFFF:
                errors.append("NEO_N4_CHAIN_ID must be a decimal non-zero u32")
    fixture_text = environment.get(LIVE_FIXTURE_VARIABLE, "").strip()
    if fixture_text and chain_id is not None:
        errors.extend(validate_live_fixture(Path(fixture_text), chain_id))
    return missing, errors, requested


def run(command: Sequence[str], *, cwd: Path, environment: Mapping[str, str]) -> tuple[subprocess.CompletedProcess[str], float]:
    print(f"+ (cd {shlex.quote(str(cwd))} && {shlex.join(command)})", flush=True)
    started = time.monotonic()
    try:
        completed = subprocess.run(
            command,
            cwd=cwd,
            env=dict(environment),
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
    except OSError as error:
        completed = subprocess.CompletedProcess(
            args=list(command),
            returncode=127,
            stdout=f"ERROR: cannot execute {command[0]}: {error}\n",
        )
    duration = time.monotonic() - started
    print(completed.stdout, end="" if completed.stdout.endswith("\n") else "\n")
    return completed, duration


def parse_trx(path: Path) -> tuple[int, int, int, int, int]:
    root = ElementTree.parse(path).getroot()
    counters = [element for element in root.iter() if element.tag.rsplit("}", 1)[-1] == "Counters"]
    if len(counters) != 1:
        raise ValueError(f"expected one TRX Counters element in {path}, found {len(counters)}")
    attributes = counters[0].attrib
    total = int(attributes["total"])
    executed = int(attributes["executed"])
    passed = int(attributes["passed"])
    failed = int(attributes["failed"])
    skipped = total - executed
    return total, executed, passed, failed, skipped


def parse_rust_result(output: str) -> tuple[int, int, int, int, int]:
    matches = list(RUST_RESULT.finditer(output))
    if not matches:
        raise ValueError("cargo test output did not contain a libtest result")
    match = matches[-1]
    passed = int(match.group("passed"))
    failed = int(match.group("failed"))
    skipped = int(match.group("ignored"))
    discovered = passed + failed + skipped
    return discovered, passed + failed, passed, failed, skipped


def parse_vitest(path: Path) -> tuple[int, int, int, int, int]:
    report = json.loads(path.read_text(encoding="utf-8"))
    discovered = int(report["numTotalTests"])
    passed = int(report["numPassedTests"])
    failed = int(report["numFailedTests"])
    skipped = int(report["numPendingTests"])
    return discovered, passed + failed, passed, failed, skipped


def parse_python(path: Path) -> tuple[int, int, int, int, int]:
    report = json.loads(path.read_text(encoding="utf-8"))
    return tuple(int(report[name]) for name in ("discovered", "executed", "passed", "failed", "skipped"))


def failed_summary(language: str, command: Sequence[str], return_code: int, duration: float) -> SuiteSummary:
    return SuiteSummary(language, 0, 0, 0, 1, 0, return_code, round(duration, 3), list(command))


def dotnet_suite(mode: str, environment: Mapping[str, str], temporary: Path) -> SuiteSummary:
    results = temporary / "dotnet"
    results.mkdir()
    command = [
        "dotnet",
        "test",
        "tests/Neo.L2.Sdk.UnitTests/Neo.L2.Sdk.UnitTests.csproj",
        "--filter",
        f"TestCategory=SdkConformance{mode.title()}",
        "/p:NuGetAudit=false",
        "--nologo",
        "--logger",
        "trx;LogFileName=sdk-conformance.trx",
        "--results-directory",
        str(results),
    ]
    neo_core_path = environment.get("NEO_CORE_PATH", "").strip()
    if neo_core_path:
        command.insert(5, f"/p:NeoCorePath={neo_core_path}")
    completed, duration = run(command, cwd=REPOSITORY_ROOT, environment=environment)
    try:
        counts = parse_trx(results / "sdk-conformance.trx")
    except (ElementTree.ParseError, KeyError, OSError, ValueError) as error:
        print(f"ERROR: cannot parse .NET conformance result: {error}", file=sys.stderr)
        return failed_summary("dotnet", command, completed.returncode or 1, duration)
    return SuiteSummary("dotnet", *counts, completed.returncode, round(duration, 3), command)


def rust_suite(mode: str, live_requested: bool, environment: Mapping[str, str], _temporary: Path) -> SuiteSummary:
    target = f"conformance_{mode}"
    command = [
        "cargo",
        "test",
        "--locked",
        "--manifest-path",
        "sdk/rust/Cargo.toml",
        "--test",
        target,
        "--",
    ]
    if mode == "live" and live_requested:
        command.append("--ignored")
    command.append("--nocapture")
    completed, duration = run(command, cwd=REPOSITORY_ROOT, environment=environment)
    try:
        counts = parse_rust_result(completed.stdout)
    except ValueError as error:
        print(f"ERROR: cannot parse Rust conformance result: {error}", file=sys.stderr)
        return failed_summary("rust", command, completed.returncode or 1, duration)
    return SuiteSummary("rust", *counts, completed.returncode, round(duration, 3), command)


def typescript_suite(mode: str, environment: Mapping[str, str], temporary: Path) -> SuiteSummary:
    report = temporary / "typescript.json"
    vitest = REPOSITORY_ROOT / "sdk" / "typescript" / "node_modules" / ".bin" / "vitest"
    command = [
        str(vitest),
        "run",
        f"tests/conformance.{mode}.test.ts",
        "--reporter=json",
        f"--outputFile={report}",
    ]
    completed, duration = run(command, cwd=REPOSITORY_ROOT / "sdk" / "typescript", environment=environment)
    try:
        counts = parse_vitest(report)
    except (KeyError, OSError, ValueError, json.JSONDecodeError) as error:
        print(f"ERROR: cannot parse TypeScript conformance result: {error}", file=sys.stderr)
        return failed_summary("typescript", command, completed.returncode or 1, duration)
    return SuiteSummary("typescript", *counts, completed.returncode, round(duration, 3), command)


def python_suite(mode: str, environment: Mapping[str, str], temporary: Path) -> SuiteSummary:
    report = temporary / "python.json"
    command = [
        sys.executable,
        "sdk/python/tests/run_conformance.py",
        "--mode",
        mode,
        "--output",
        str(report),
    ]
    completed, duration = run(command, cwd=REPOSITORY_ROOT, environment=environment)
    try:
        counts = parse_python(report)
    except (KeyError, OSError, ValueError, json.JSONDecodeError) as error:
        print(f"ERROR: cannot parse Python conformance result: {error}", file=sys.stderr)
        return failed_summary("python", command, completed.returncode or 1, duration)
    return SuiteSummary("python", *counts, completed.returncode, round(duration, 3), command)


def validation_errors(summary: SuiteSummary, mode: str, live_requested: bool) -> list[str]:
    errors: list[str] = []
    if summary.return_code != 0:
        errors.append(f"{summary.language}: command exited {summary.return_code}")
    if summary.discovered < 1:
        errors.append(f"{summary.language}: suite discovered zero tests")
    if summary.failed != 0:
        errors.append(f"{summary.language}: {summary.failed} tests failed")
    if summary.discovered > 0 and summary.discovered != summary.executed + summary.skipped:
        errors.append(
            f"{summary.language}: inconsistent counters: discovered != executed + skipped"
        )
    if summary.discovered > 0 and summary.executed != summary.passed + summary.failed:
        errors.append(
            f"{summary.language}: inconsistent counters: executed != passed + failed"
        )

    must_execute = mode == "offline" or live_requested
    if must_execute:
        if summary.executed != summary.discovered:
            errors.append(
                f"{summary.language}: expected {summary.discovered} executed tests, found {summary.executed}"
            )
        if summary.passed != summary.discovered:
            errors.append(
                f"{summary.language}: expected {summary.discovered} passed tests, found {summary.passed}"
            )
        if summary.skipped != 0:
            errors.append(f"{summary.language}: {summary.skipped} tests were skipped")
    else:
        if summary.executed != 0 or summary.passed != 0:
            errors.append(f"{summary.language}: live tests executed without complete endpoint configuration")
        if summary.skipped != summary.discovered:
            errors.append(
                f"{summary.language}: expected all {summary.discovered} live tests to skip, found {summary.skipped}"
            )
    return errors


def append_github_summary(report: dict[str, object]) -> None:
    destination = os.environ.get("GITHUB_STEP_SUMMARY")
    if not destination:
        return
    suites = report["suites"]
    lines = [
        f"## SDK conformance: {report['mode']} — {report['status']}",
        "",
        "| Language | Discovered | Executed | Passed | Skipped | Failed |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for suite in suites:
        lines.append(
            f"| {suite['language']} | {suite['discovered']} | {suite['executed']} | "
            f"{suite['passed']} | {suite['skipped']} | {suite['failed']} |"
        )
    if report["missingLiveVariables"]:
        lines.extend(["", f"Explicitly skipped: missing `{', '.join(report['missingLiveVariables'])}`."])
    if report["errors"]:
        lines.extend(["", "Errors:", *[f"- {error}" for error in report["errors"]]])
    with open(destination, "a", encoding="utf-8") as output:
        output.write("\n".join(lines) + "\n")


def parse_arguments(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=("offline", "live"), required=True)
    parser.add_argument("--language", choices=LANGUAGES, action="append", dest="languages")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--require-live", action="store_true")
    parser.add_argument(
        "--require-all-languages",
        action="store_true",
        help="fail unless dotnet, rust, typescript, and python are all selected",
    )
    arguments = parser.parse_args(argv)
    if arguments.require_live and arguments.mode != "live":
        parser.error("--require-live is only valid with --mode live")
    return arguments


def main(argv: Sequence[str] | None = None) -> int:
    arguments = parse_arguments(sys.argv[1:] if argv is None else argv)
    languages = list(dict.fromkeys(arguments.languages or LANGUAGES))
    canonical_errors = validate_canonical_vectors()
    if arguments.require_all_languages and (
        len(languages) != len(LANGUAGES) or set(languages) != set(LANGUAGES)
    ):
        canonical_errors.append(
            f"all four SDK languages are required, selected {', '.join(languages)}"
        )
    if arguments.mode == "live":
        missing, configuration_errors, live_requested = live_configuration(os.environ)
    else:
        missing, configuration_errors, live_requested = [], [], False
    live_configured = arguments.mode == "live" and live_requested and not configuration_errors
    environment = os.environ.copy()
    environment["NEO_SDK_CONFORMANCE_VECTORS"] = str(VECTOR_PATH)

    runners = {
        "dotnet": lambda temporary: dotnet_suite(arguments.mode, environment, temporary),
        "rust": lambda temporary: rust_suite(arguments.mode, live_requested, environment, temporary),
        "typescript": lambda temporary: typescript_suite(arguments.mode, environment, temporary),
        "python": lambda temporary: python_suite(arguments.mode, environment, temporary),
    }
    summaries: list[SuiteSummary] = []
    with tempfile.TemporaryDirectory(prefix="neo-n4-sdk-conformance-") as temporary_directory:
        temporary = Path(temporary_directory)
        for language in languages:
            summaries.append(runners[language](temporary))

    errors = [*canonical_errors, *configuration_errors, *[
        error
        for summary in summaries
        for error in validation_errors(summary, arguments.mode, live_requested)
    ]]
    if arguments.mode == "live" and arguments.require_live and missing:
        errors.append(f"live conformance is required but missing {', '.join(missing)}")

    if errors:
        status = "failed"
    elif arguments.mode == "live" and not live_configured:
        status = "skipped"
    else:
        status = "passed"
    report: dict[str, object] = {
        "schema": "neo-n4-sdk-conformance-summary/v1",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "mode": arguments.mode,
        "status": status,
        "liveConfigured": live_configured,
        "liveRequired": arguments.require_live,
        "allLanguagesRequired": arguments.require_all_languages,
        "missingLiveVariables": missing,
        "configurationErrors": configuration_errors,
        "vectorPath": str(VECTOR_PATH.relative_to(REPOSITORY_ROOT)),
        "vectorSha256": hashlib.sha256(VECTOR_PATH.read_bytes()).hexdigest(),
        "liveFixtureSha256": (
            hashlib.sha256(Path(os.environ[LIVE_FIXTURE_VARIABLE]).read_bytes()).hexdigest()
            if live_configured
            else None
        ),
        "commitSha": os.environ.get("GITHUB_SHA"),
        "scenarioCounts": {
            "offlineRpcCases": len(EXPECTED_VECTOR_CASES),
            "offlineEnvelopeErrors": len(EXPECTED_ENVELOPE_ERRORS),
            "offlineResponseErrors": len(EXPECTED_RESPONSE_ERRORS),
            "liveRpcCases": len(EXPECTED_LIVE_CASES),
            "liveBaseNodes": 2,
        },
        "suites": [asdict(summary) for summary in summaries],
        "errors": errors,
    }
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    append_github_summary(report)
    print(json.dumps(report, indent=2))
    return 0 if status != "failed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
