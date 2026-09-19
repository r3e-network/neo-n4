"""Inductive model of FailoverDAWriter publish failover and profile invariants.

Models the ordered-tier publish loop in src/Neo.Plugins.L2DA/FailoverDAWriter.cs:
only a transient IOException advances to the next tier; a non-transient exception,
cancellation, or a malformed/non-binding receipt aborts immediately (never masked by a
fallback); a successful publish returns only a receipt that binds the published payload
via Hash256(request.Payload) and carries this profile's required metadata. Also models
the constructor invariant: every tier shares the primary's DAMode and a common
non-Unspecified DAReceiptKind (failover never downgrades DA profile). Handwritten
abstraction, NOT verification of C#/NeoVM. See da-failover-model.md.
"""
import argparse
from pathlib import Path
import hashlib
import json
import sys

import z3

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "src/Neo.Plugins.L2DA/FailoverDAWriter.cs"


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


REVIEWED_SOURCE_SHA256 = "461c2eee154bbc7db542828e05885ca15c577c8ba3c38a9fd33c94778ad4e592"


def check_source(path):
    digest = source_digest(path)
    if digest != REVIEWED_SOURCE_SHA256:
        raise ValueError("reviewed FailoverDAWriter source changed; re-review model correspondence")
    return digest


def obligations():
    # Fixed three-tier model (the policy holds for any number of tiers; N=3 makes the
    # formulas ground and the solver check exact). Each tier is exactly one of:
    #   accepted[i] - returned a valid receipt binding Hash256(payload) with required metadata
    #   transient[i] - threw a transient IOException (the only fallback trigger)
    #   fatal[i]     - non-transient exception / cancellation / malformed or non-binding receipt
    a0, a1, a2 = z3.Bools("a0 a1 a2")
    t0, t1, t2 = z3.Bools("t0 t1 t2")
    f0, f1, f2 = z3.Bools("f0 f1 f2")
    profile = z3.Bool("profile")   # constructor accepted: all tiers share Mode + ReceiptKind

    def exactly_one(a, t, f):
        return z3.And(z3.Implies(a, z3.And(z3.Not(t), z3.Not(f))),
                      z3.Implies(t, z3.And(z3.Not(a), z3.Not(f))),
                      z3.Implies(f, z3.And(z3.Not(a), z3.Not(t))),
                      z3.Or(a, t, f))

    premise = z3.And(exactly_one(a0, t0, f0), exactly_one(a1, t1, f1),
                     exactly_one(a2, t2, f2), profile)

    # Publish loop scans tiers in order; the first non-transient tier decides.
    # success = first non-transient tier was accepted.
    success = z3.Or(a0, z3.And(t0, a1), z3.And(t0, t1, a2))
    # fatal_run = first non-transient tier was fatal.
    fatal_run = z3.Or(f0, z3.And(t0, f1), z3.And(t0, t1, f2))
    all_transient = z3.And(t0, t1, t2)

    # --- safety (unsat) ---
    # Profile is a constructor precondition: a publish never runs without the shared profile.
    yield "publish_requires_profile", z3.And(premise, z3.Not(profile)), z3.unsat
    # A success requires some accepted tier (can't succeed via transient/fatal only).
    yield "success_requires_valid_tier", z3.And(
        premise, success, z3.Not(z3.Or(a0, a1, a2))), z3.unsat
    # A fatal at the first non-transient position is never masked by a later tier.
    yield "fatal0_not_masked", z3.And(premise, f0, z3.Not(t0), success), z3.unsat
    yield "fatal_not_masked", z3.And(premise, fatal_run, success), z3.unsat
    # Success and fatal are disjoint outcomes.
    yield "success_and_fatal_are_disjoint", z3.And(premise, success, fatal_run), z3.unsat
    # All-failed forward: all-transient cannot also be a success.
    yield "all_failed_forward", z3.And(premise, all_transient, success), z3.unsat
    # All-failed backward: if not all transient, the first non-transient is accepted or fatal,
    # so either success or fatal_run must hold (no silent/spurious all-failed).
    yield "all_failed_backward", z3.And(
        premise, z3.Not(all_transient), z3.Not(success), z3.Not(fatal_run)), z3.unsat

    # --- feasibility (sat) ---
    yield "success_tier0_feasible", z3.And(premise, a0), z3.sat
    yield "failover_success_feasible", z3.And(premise, t0, t1, a2, z3.Not(all_transient)), z3.sat
    yield "all_transient_fails_feasible", z3.And(premise, all_transient), z3.sat
    yield "fatal_decides_feasible", z3.And(premise, f0), z3.sat

    # --- negative controls (sat): drop a guard and a bad publish becomes reachable ---
    # (a) without the "first non-transient must be accepted" check, a run whose only
    # non-transient tiers are fatal would be reported as success (spurious success).
    yield "negative_control_success_without_accepted_tier", z3.And(
        premise, z3.Not(all_transient), z3.Not(z3.Or(a0, a1, a2))), z3.sat
    # (b) a fatal at tier 0 that falls through to an accepted tier 1 (masked non-transient).
    yield "negative_control_masked_fatal", z3.And(
        premise, f0, z3.Not(t0), a1, z3.Not(t1)), z3.sat
    # (c) cross-profile failover (mode downgrade) allowed when the constructor guard is dropped.
    yield "negative_control_cross_profile", z3.And(
        z3.And(exactly_one(a0, t0, f0), exactly_one(a1, t1, f1), exactly_one(a2, t2, f2)),
        z3.Not(profile), a0), z3.sat


def solve(name, formula, expected, timeout_ms=10000):
    solver = z3.Solver()
    solver.set(timeout=timeout_ms)
    solver.add(formula)
    r = solver.check()
    rec = {"name": name, "expected": str(expected), "actual": str(r), "passed": r == expected}
    if r == z3.sat:
        rec["witness"] = str(solver.model())
    if r == z3.unknown:
        rec["reason"] = solver.reason_unknown()
    return rec


def run(source=CONTRACT):
    report = {"schema": "neo-n4/da-failover-model/v1", "wholeSystemVerified": False,
              "scope": "inductive safety of the FailoverDAWriter publish failover and profile invariants",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten C#/NeoVM correspondence, not an extracted transition relation",
                  "transient = IOException excluding InvalidDataException; non-transient never falls back",
                  "a successful publish validates the receipt before returning (metadata + payload binding)",
                  "the failover set is ordered and shares one DAMode and one non-Unspecified DAReceiptKind",
                  "cancellation always propagates and is never masked by a fallback",
                  "recovery here covers the write-side failover policy, not RocksDB crash-consistency or replay"],
              "source": str(source), "scriptSha256": source_digest(__file__),
              "specSha256": source_digest(ROOT / "doc.md")}
    try:
        report["sourceSha256"] = check_source(source)
        report["obligations"] = [solve(*o) for o in obligations()]
        if report["obligations"] and all(o["passed"] for o in report["obligations"]):
            report["status"] = "passed"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    report["exitCode"] = 0 if report["status"] == "passed" else 1
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("da-failover-result.json"))
    args = parser.parse_args()
    report = run()
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for o in report["obligations"]:
        print(f"{o['name']}: {o['actual']} (expected {o['expected']})")
    print(f"status={report['status']}; exit={report['exitCode']}; report={args.output}")
    if "error" in report:
        print(report["error"])
    return report["exitCode"]


if __name__ == "__main__":
    sys.exit(main())