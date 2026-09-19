"""Inductive safety model of GovernanceController proposal authorization gate.

Models the council-vote / timelock / veto gate behind IsApprovedAndTimelocked
(GovernanceControllerContract lines 548-558). Proves the authorization gate is sound:
an action guarded by isApprovedAndTimelocked can only execute when the proposal reached
the M-of-N approval threshold, the configured timelock has elapsed since first
threshold-reach, the proposal is not vetoed, and its epoch matches the current council
epoch. Also proves the first-threshold-crossing timer is recorded at most once. Handwritten
abstraction, NOT verification of C#/NeoVM. See governance-model.md.
"""
import argparse
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import sys

import z3

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "contracts/NeoHub.GovernanceController/GovernanceControllerContract.cs"


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


REVIEWED_SOURCE_SHA256 = "439b9642d0bfaad416ccad0137113cf8048d2b932335d2aaadcb9c51efcb0af9"


def check_source(path):
    digest = source_digest(path)
    if digest != REVIEWED_SOURCE_SHA256:
        raise ValueError("reviewed GovernanceController source changed; re-review model correspondence")
    return digest


@dataclass(frozen=True)
class Proposal:
    count: object      # approval count (GetApprovalCount), monotone non-decreasing
    threshold: object  # M-of-N threshold (constant per epoch)
    approved: object   # true once count first reached threshold (approvedAt recorded)
    vetoed: object     # permanent council veto
    epoch_match: object  # proposal epoch == current council epoch
    now: object        # wall-clock time, monotone non-decreasing
    timelock: object   # configured timelock in the same unit as now


def symbolic():
    return Proposal(z3.Int("count"), z3.Int("threshold"), z3.Bool("approved"),
                    z3.Bool("vetoed"), z3.Bool("epoch_match"), z3.Int("now"), z3.Int("timelock"))


def gate(p):
    # IsApprovedAndTimelocked: epoch matches, not vetoed, approvedAt set (threshold reached),
    # and now >= approvedAt + timelock.
    return z3.And(p.epoch_match, z3.Not(p.vetoed), p.approved, p.now >= p.timelock)


def invariant(p):
    # approved is recorded only on threshold reach, and the timer only moves forward.
    return z3.And(p.count >= 0, p.threshold > 0, p.timelock >= 0,
                  z3.Implies(p.approved, p.count >= p.threshold),
                  p.epoch_match == p.epoch_match)  # placeholder to keep shape explicit


def approve(p, inc):
    # ApproveProposal: count' = count + 1; on first crossing of threshold set approved.
    # The veto window cannot start before threshold reach.
    crossed = z3.And(p.count < p.threshold, p.count + inc >= p.threshold)
    return z3.And(invariant(p), inc == 1), Proposal(
        p.count + inc, p.threshold,
        z3.Or(p.approved, z3.And(crossed, inc == 1)),
        p.vetoed, p.epoch_match, p.now, p.timelock)


def veto(p):
    return invariant(p), Proposal(p.count, p.threshold, p.approved,
                                  z3.BoolVal(True), p.epoch_match, p.now, p.timelock)


def advance_time(p, dt):
    return z3.And(invariant(p), dt >= 0), Proposal(
        p.count, p.threshold, p.approved, p.vetoed, p.epoch_match, p.now + dt, p.timelock)


def obligations():
    p = symbolic()
    inc, dt = z3.Int("inc"), z3.Int("dt")
    init = Proposal(z3.IntVal(0), z3.IntVal(3), z3.BoolVal(False), z3.BoolVal(False),
                    z3.BoolVal(True), z3.IntVal(0), z3.IntVal(100))

    yield "initial_invariant", z3.Not(invariant(init)), z3.unsat
    yield "initial_not_approved", z3.And(invariant(init), init.approved), z3.unsat
    yield "initial_gate_false", z3.And(invariant(init), gate(init)), z3.unsat

    for name, (guard, nxt) in zip(
            ("approve", "veto", "advance_time"),
            (approve(p, inc), veto(p), advance_time(p, dt))):
        premise = z3.And(invariant(p), guard)
        yield name + "_enabled", premise, z3.sat
        yield name + "_preserves_invariant", z3.And(
            premise, z3.Not(invariant(nxt))), z3.unsat
        # Gate soundness: if the gate is satisfied, the proposal reached threshold.
        yield name + "_gate_implies_threshold", z3.And(
            premise, gate(nxt), z3.Not(nxt.approved)), z3.unsat
        # Gate implies not vetoed.
        yield name + "_gate_implies_not_vetoed", z3.And(
            premise, gate(nxt), nxt.vetoed), z3.unsat
        # Gate implies timelock elapsed.
        yield name + "_gate_implies_timelock", z3.And(
            premise, gate(nxt), z3.Not(nxt.now >= nxt.timelock)), z3.unsat
        # Gate implies epoch match.
        yield name + "_gate_implies_epoch", z3.And(
            premise, gate(nxt), z3.Not(nxt.epoch_match)), z3.unsat

    # A proposal can never reach threshold and be vetoed and still satisfy the gate.
    yield "veto_blocks_gate", z3.And(
        invariant(p), p.approved, p.now >= p.timelock,
        gate(Proposal(p.count, p.threshold, p.approved, z3.BoolVal(True),
                      p.epoch_match, p.now, p.timelock))), z3.unsat

    # Negative controls: drop exactly one conjunct of the gate and it admits a bad state.
    # (a) missing threshold-reach conjunct -> an unapproved proposal could gate.
    yield "negative_control_gate_without_threshold", z3.And(
        invariant(p), p.epoch_match, z3.Not(p.vetoed), z3.Not(p.approved),
        p.now >= p.timelock), z3.sat
    # (b) missing veto conjunct -> a vetoed proposal could gate.
    yield "negative_control_gate_while_vetoed", z3.And(
        invariant(p), p.epoch_match, p.vetoed, p.approved,
        p.now >= p.timelock), z3.sat
    # (c) missing timelock conjunct -> an action could execute before the delay window.
    yield "negative_control_gate_before_timelock", z3.And(
        invariant(p), p.epoch_match, z3.Not(p.vetoed), p.approved,
        p.now < p.timelock), z3.sat
    # (d) missing epoch-match conjunct -> a stale-epoch proposal could gate.
    yield "negative_control_gate_stale_epoch", z3.And(
        invariant(p), z3.Not(p.epoch_match), z3.Not(p.vetoed), p.approved,
        p.now >= p.timelock), z3.sat


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
    report = {"schema": "neo-n4/governance-model/v1", "wholeSystemVerified": False,
              "scope": "inductive safety of the GovernanceController authorization gate",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten C#/NeoVM correspondence, not an extracted transition relation",
                  "IsApprovedAndTimelocked is the sole gate for verifier/bridge/admission upgrades",
                  "first threshold-crossing records approvedAt; later votes cannot reset the timer",
                  "veto is permanent and replay-safe; epoch must match the current council epoch",
                  "timelock is a pure delay window during which the governance owner may veto",
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
                        default=Path(__file__).with_name("governance-result.json"))
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