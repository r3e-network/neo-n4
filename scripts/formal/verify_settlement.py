"""Inductive safety model of an already-registered RollupHub chain.

Handwritten abstraction, NOT verification of C#/NeoVM. See settlement-model.md.
Array indices and heights are mathematical integers; roots are 256-bit vectors.
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
REVIEWED_SOURCE_SHA256 = "10504d10c0f74669f15fe9455a4d3426f8bd5967b36221654976bec574bd93a3"
MAX_HEIGHT = 2**64 - 1


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


def check_source(path):
    digest = source_digest(path)
    if digest != REVIEWED_SOURCE_SHA256:
        raise ValueError("reviewed RollupHub source changed; re-review model correspondence")
    return digest


@dataclass(frozen=True)
class State:
    height: object
    root: object
    pending: object
    pending_number: object
    pending_pre: object
    pending_post: object
    pending_verified: object
    pre: object
    post: object
    verified: object


def symbolic_state():
    root_type = z3.BitVecSort(256)
    return State(z3.Int("height"), z3.BitVec("root", 256), z3.Bool("pending"),
                 z3.Int("pending_number"), z3.BitVec("pending_pre", 256),
                 z3.BitVec("pending_post", 256), z3.Bool("pending_verified"),
                 z3.Array("pre", z3.IntSort(), root_type),
                 z3.Array("post", z3.IntSort(), root_type),
                 z3.Array("verified", z3.IntSort(), z3.BoolSort()))


def invariant(state, genesis, index):
    # index is arbitrary, not a sampled or bounded enumeration of batch heights.
    return z3.And(
        genesis != 0,
        state.height >= 0, state.height <= MAX_HEIGHT,
        state.root == z3.If(state.height == 0, genesis, state.post[state.height]),
        z3.Implies(z3.And(index >= 1, index <= state.height), z3.And(
            state.verified[index],
            state.pre[index] == z3.If(index == 1, genesis, state.post[index - 1]))),
        z3.Implies(state.pending, z3.And(
            state.height < MAX_HEIGHT,
            state.pending_number == state.height + 1,
            state.pending_pre == state.root,
            state.pending_verified)))


def finalize(state, number, pre, post, verified, wrong_root=False):
    return replace(state, height=number,
                   root=state.root if wrong_root else post,
                   pending=z3.BoolVal(False),
                   pre=z3.Store(state.pre, number, pre),
                   post=z3.Store(state.post, number, post),
                   verified=z3.Store(state.verified, number, verified))


def obligations():
    state = symbolic_state()
    genesis = z3.BitVec("genesis", 256)
    index = z3.Int("index")
    number = z3.Int("number")
    pre, post = z3.BitVecs("input_pre input_post", 256)
    proof_ok, operational, owner, da_fresh = z3.Bools("proof_ok operational owner da_fresh")
    inv = invariant(state, genesis, index)
    base = replace(state, height=z3.IntVal(0), root=genesis, pending=z3.BoolVal(False))
    common = z3.And(operational, da_fresh, z3.Not(state.pending), proof_ok,
                    state.height < MAX_HEIGHT, number == state.height + 1)
    submit_guard = z3.And(common, pre == state.root)
    submitted = replace(state, pending=z3.BoolVal(True), pending_number=number,
                        pending_pre=pre, pending_post=post, pending_verified=proof_ok)
    finalize_guard = z3.And(operational, owner, state.pending, number == state.height + 1)
    finalized = finalize(state, state.pending_number, state.pending_pre,
                         state.pending_post, state.pending_verified)
    atomic = finalize(state, number, pre, post, proof_ok)
    yield "initial_domain_nonempty", genesis != 0, z3.sat
    yield "initial_invariant", z3.And(genesis != 0, z3.Not(invariant(base, genesis, index))), z3.unsat
    for name, guard, next_state in (
        ("submit", submit_guard, submitted),
        ("finalize", finalize_guard, finalized),
        ("atomic", submit_guard, atomic),
        ("fault_stutter", z3.BoolVal(True), state),
    ):
        premise = z3.And(inv, guard)
        yield name + "_enabled", premise, z3.sat
        yield name + "_preserves_invariant", z3.And(
            premise, z3.Not(invariant(next_state, genesis, index))), z3.unsat
        yield name + "_preserves_finalized_history", z3.And(
            premise, index >= 1, index <= state.height,
            z3.Or(next_state.pre[index] != state.pre[index],
                  next_state.post[index] != state.post[index],
                  next_state.verified[index] != state.verified[index])), z3.unsat
        yield name + "_height_monotone", z3.And(premise, next_state.height < state.height), z3.unsat

    # The same invariant must fail when a load-bearing guard or update is removed.
    yield "negative_control_missing_pre_root_guard", z3.And(
        inv, common, index == number, pre != state.root,
        z3.Not(invariant(atomic, genesis, index))), z3.sat
    wrong = finalize(state, number, pre, post, proof_ok, wrong_root=True)
    yield "negative_control_missing_canonical_root_update", z3.And(
        inv, submit_guard, post != state.root,
        z3.Not(invariant(wrong, genesis, index))), z3.sat
    unverified = finalize(state, number, pre, post, z3.BoolVal(False))
    yield "negative_control_unverified_finalization", z3.And(
        inv, submit_guard, index == number,
        z3.Not(invariant(unverified, genesis, index))), z3.sat


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
    report = {"schema": "neo-n4/settlement-model/v1", "wholeSystemVerified": False,
              "scope": "inductive safety of handwritten registered-chain settlement abstraction",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten C#/NeoVM correspondence, not an extracted transition relation",
                  "chain and nonzero immutable genesis already registered",
                  "atomic fault rollback, no reentrancy or hidden storage writers",
                  "height arithmetic does not wrap; terminal height has no successor",
                  "proof_ok models successful external verification, not cryptographic soundness",
                  "no governance rollback, reconfiguration or concurrency transitions"],
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
                        default=Path(__file__).with_name("settlement-result.json"))
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
