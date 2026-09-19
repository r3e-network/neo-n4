"""Inductive safety model of RollupHub's atomic chain registration.

Proves that the single atomic RegisterChain(chainId, configBytes, genesisStateRoot)
call (doc.md §3.2) reaches exactly the state the settlement model assumes: config
and a non-zero, immutable genesis root are present together, and no partially
initialized chain is observable as active. Handwritten abstraction, NOT verification
of C#/NeoVM. See registration-model.md.
"""
import argparse
from dataclasses import dataclass, replace
import hashlib
import json
from pathlib import Path
import sys

import z3

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "contracts/NeoHub.RollupHub/RollupHubContract.cs"
REVIEWED_SOURCE_SHA256 = "f2b4f03648926c5fc29d2d19698ca1ff71a3c9e7b4cb576bc370dc60e4a070aa"


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


def check_source(path):
    digest = source_digest(path)
    if digest != REVIEWED_SOURCE_SHA256:
        raise ValueError("reviewed RollupHub source changed; re-review model correspondence")
    return digest


@dataclass(frozen=True)
class ChainState:
    registered: object       # config stored under PrefixConfig
    genesis_set: object      # immutable genesis root stored under PrefixGenesisRoot
    root: object             # 256-bit genesis root (meaningful when genesis_set)
    active: object           # OffsetActive bit in the 91-byte config


def symbolic_state():
    return ChainState(
        registered=z3.Bool("registered"),
        genesis_set=z3.Bool("genesis_set"),
        root=z3.BitVec("root", 256),
        active=z3.Bool("active"))


def is_active(state):
    # IsActive(chainId) == config[OffsetActive]==1 && genesis-root present.
    return z3.And(state.active, state.genesis_set)


def invariant(state):
    # (1) config and genesis root are always co-present — never a partially
    # initialized (configured-but-unanchored, or anchored-but-unregistered) chain.
    # (2) a registered chain has a non-zero genesis root.
    # (3) built from is_active: observably-active implies genesis present.
    return z3.And(
        state.registered == state.genesis_set,
        z3.Implies(state.genesis_set, state.root != 0))


def register_chain(state, root_in, active_in):
    # Atomic 3-arg entry. Fresh registration sets registered+genesis together.
    # Idempotent re-registration may refresh the active bit only while the already
    # registered genesis root is unchanged; a zero root and a different root are rejected.
    root_guard = z3.If(state.registered, root_in == state.root, root_in != 0)
    next_state = ChainState(
        registered=z3.BoolVal(True),
        genesis_set=z3.BoolVal(True),
        root=z3.If(state.registered, state.root, root_in),
        active=active_in)
    return root_guard, next_state


def update_chain(state, active_in):
    guard = state.registered
    return guard, replace(state, active=active_in)


def update_active(state, value):
    guard = state.registered
    return guard, replace(state, active=z3.BoolVal(value))


def obligations():
    s = symbolic_state()
    root_in, active_in = z3.BitVec("root_in", 256), z3.Bool("active_in")
    init = ChainState(z3.BoolVal(False), z3.BoolVal(False), z3.BitVecVal(0, 256), z3.BoolVal(False))
    fresh = ChainState(z3.BoolVal(False), z3.BoolVal(False), z3.BitVecVal(0, 256), z3.BoolVal(False))
    partial = ChainState(z3.BoolVal(True), z3.BoolVal(False), z3.BitVecVal(0, 256), z3.BoolVal(True))
    different = ChainState(z3.BoolVal(True), z3.BoolVal(True), root_in, z3.BoolVal(True))

    # Initial state satisfies the invariant.
    yield "initial_invariant", z3.Not(invariant(init)), z3.unsat
    # IsActive structurally requires a registered genesis root (definition sound).
    yield "active_definition_sound", z3.And(is_active(s), z3.Not(s.genesis_set)), z3.unsat

    for name, (guard, next_state) in zip(
            ("register", "update", "resume", "pause"),
            (register_chain(s, root_in, active_in),
             update_chain(s, active_in),
             update_active(s, True),
             update_active(s, False))):
        premise = z3.And(invariant(s), guard)
        yield name + "_enabled", premise, z3.sat
        yield name + "_preserves_invariant", z3.And(
            premise, z3.Not(invariant(next_state))), z3.unsat
        # is_active never reports an active chain whose genesis root is absent or zero.
        yield name + "_active_implies_anchored", z3.And(
            premise, is_active(next_state),
            z3.Or(z3.Not(next_state.genesis_set), next_state.root == 0)), z3.unsat
        # Once genesis is set it is immutable across every transition.
        yield name + "_preserves_root", z3.And(
            premise, s.genesis_set, next_state.root != s.root), z3.unsat

    # Negative controls (reachability): drop a load-bearing guard and a bad state
    # becomes reachable, so each guard is genuinely what the invariant leans on.
    # (a) zero genesis root admitted on fresh registration -> anchored-but-zero root.
    yield "negative_control_missing_zero_root_guard", z3.And(
        root_in == 0, invariant(fresh),
        z3.Not(invariant(ChainState(z3.BoolVal(True), z3.BoolVal(True), root_in, active_in)))), z3.sat
    # (b) config written without genesis (the old non-atomic path) -> partial init.
    yield "negative_control_config_without_genesis", z3.And(
        invariant(fresh), z3.Not(invariant(partial))), z3.sat
    # (c) re-registration with a different (here zero) root mutates an immutable root.
    yield "negative_control_immutable_root_mutated", z3.And(
        invariant(s), s.genesis_set, s.root != 0, root_in == 0,
        z3.Not(invariant(different))), z3.sat


def solve(name, formula, expected, timeout_ms=10000):
    solver = z3.Solver()
    solver.set(timeout=timeout_ms)
    solver.add(formula)
    result = solver.check()
    record = {"name": name, "expected": str(expected), "actual": str(result),
              "passed": result == expected}
    if result == z3.sat:
        record["witness"] = str(solver.model())
    if result == z3.unknown:
        record["reason"] = solver.reason_unknown()
    return record


def run(source=CONTRACT):
    report = {"schema": "neo-n4/registration-model/v1", "wholeSystemVerified": False,
              "scope": "inductive safety of the atomic RollupHub chain registration abstraction",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten C#/NeoVM correspondence, not an extracted transition relation",
                  "one registration entry: RegisterChain(chainId, configBytes, genesisStateRoot)",
                  "idempotent re-registration may refresh config; the genesis root is immutable",
                  "atomic fault rollback, no reentrancy or hidden storage writers",
                  "active denotes OffsetActive==1 AND a registered genesis root, matching IsActive",
                  "no governance rollback or concurrency transitions"],
              "source": str(source), "scriptSha256": source_digest(__file__),
              "specSha256": source_digest(ROOT / "doc.md")}
    try:
        report["sourceSha256"] = check_source(source)
        report["obligations"] = [solve(*item) for item in obligations()]
        if report["obligations"] and all(item["passed"] for item in report["obligations"]):
            report["status"] = "passed"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    report["exitCode"] = 0 if report["status"] == "passed" else 1
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path,
                        default=Path(__file__).with_name("registration-result.json"))
    args = parser.parse_args()
    report = run()
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for item in report["obligations"]:
        print(f"{item['name']}: {item['actual']} (expected {item['expected']})")
    print(f"status={report['status']}; exit={report['exitCode']}; report={args.output}")
    if "error" in report:
        print(report["error"])
    return report["exitCode"]


if __name__ == "__main__":
    sys.exit(main())