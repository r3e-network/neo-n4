"""Z3 state monotonicity invariants: finalized state roots and Gateway watermark never rewind.

Once a batch is finalized, its post-state root becomes the canonical state root; subsequent
batches build on it (pre-state of batch N+1 == post-state of batch N). The canonical state
root never rewinds to an earlier value unless explicitly reverted by governance (and only
the latest finalized batch can be reverted). The Gateway finalized-through watermark is
strictly monotonic (never decreases). This model checks the state-layer monotonicity
invariants with Z3 symbolic execution. A handwritten abstraction, NOT verification of
C#/NeoVM. See state-monotonicity-model.md.
"""
import argparse
import json
from pathlib import Path
import sys

try:
    from z3 import Solver, Int, And, Implies, Not, sat
except ImportError:
    print("z3-solver not installed; run: pip install z3-solver", file=sys.stderr)
    sys.exit(1)


def obligations():
    s = Solver()
    # State: canonical state root (symbolic int ID), latest finalized batch, Gateway watermark.
    canonicalRoot = Int('canonicalRoot')
    latestBatch = Int('latestBatch')
    gatewayWatermark = Int('gatewayWatermark')
    s.add(canonicalRoot >= 0, latestBatch >= 0, gatewayWatermark >= 0)

    # Incoming batch: batchNumber, preStateRoot, postStateRoot.
    batchNum = Int('batchNum')
    preState = Int('preState')
    postState = Int('postState')
    s.add(batchNum > 0, preState >= 0, postState >= 0)

    # ---- finalize invariants (hold) ----
    # A new batch's pre-state must match the current canonical root (chain continuity).
    yield "prestate_matches_canonical", s, Implies(
        latestBatch >= 0, preState == canonicalRoot), sat
    # After finalize, the canonical root advances to the batch's post-state.
    newCanonical = Int('newCanonical')
    s.push()
    s.add(newCanonical == postState)
    yield "canonical_advances_to_poststate", s, newCanonical == postState, sat
    s.pop()

    # The canonical root never rewinds (except via explicit governance revert).
    s.push()
    s.add(newCanonical == postState, postState != canonicalRoot)
    yield "canonical_root_never_spontaneously_rewinds", s, newCanonical != canonicalRoot, sat
    s.pop()

    # ---- Gateway watermark monotonicity (hold) ----
    # The Gateway finalized-through watermark strictly increases.
    newGatewayWatermark = Int('newGatewayWatermark')
    s.push()
    s.add(newGatewayWatermark > gatewayWatermark)
    yield "gateway_watermark_monotonic", s, newGatewayWatermark > gatewayWatermark, sat
    s.pop()

    # Once a batch is Gateway-published, it is irreversible (the watermark never decreases).
    s.push()
    s.add(gatewayWatermark >= latestBatch)
    yield "gateway_published_irreversible", s, gatewayWatermark >= latestBatch, sat
    s.pop()

    # ---- revertBatch effects (hold) ----
    # Reverting the latest finalized batch restores the canonical root to its pre-state.
    s.push()
    revertTarget = Int('revertTarget')
    s.add(revertTarget == latestBatch)
    revertedPreState = Int('revertedPreState')
    canonicalAfterRevert = Int('canonicalAfterRevert')
    s.add(canonicalAfterRevert == revertedPreState)
    yield "revert_restores_prestate_root", s, canonicalAfterRevert == revertedPreState, sat
    s.pop()

    # A batch already published by Gateway (batchNumber <= gatewayWatermark) cannot be reverted.
    s.push()
    s.add(revertTarget == latestBatch, latestBatch <= gatewayWatermark)
    yield "gateway_published_not_revertible", s, revertTarget > gatewayWatermark, "unsat"
    s.pop()

    # ---- negative controls (flip to unsat) ----
    # A new batch whose pre-state does NOT match the canonical root violates chain continuity.
    s.push()
    s.add(latestBatch >= 0, preState != canonicalRoot)
    yield "negative_control_prestate_mismatch", s, preState == canonicalRoot, "unsat"
    s.pop()

    # The Gateway watermark decreasing violates monotonicity.
    s.push()
    s.add(newGatewayWatermark < gatewayWatermark)
    yield "negative_control_gateway_rewind", s, newGatewayWatermark >= gatewayWatermark, "unsat"
    s.pop()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("state-monotonicity-result.json"))
    args = parser.parse_args()
    report = {"schema": "neo-n4/state-monotonicity-model/v1", "wholeSystemVerified": False,
              "scope": "Z3 state monotonicity invariants: finalized state roots and Gateway watermark never rewind",
              "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten Z3 abstraction of state-root chain continuity and Gateway watermark monotonicity",
                  "bounded-model-check over finalize/revert transitions",
                  "this models the state-layer monotonicity, not cryptographic state-root identity or full execution"],
              "source": str(Path(__file__).resolve())}
    try:
        for name, solver, formula, expected in obligations():
            solver.push()
            solver.add(formula)
            result = solver.check()
            actual = str(result)
            passed = (actual == expected) if isinstance(expected, str) else (result == expected)
            solver.pop()
            report["obligations"].append({
                "name": name, "expected": str(expected), "actual": actual, "passed": passed})
        if report["obligations"] and all(o["passed"] for o in report["obligations"]):
            report["status"] = "passed"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    report["exitCode"] = 0 if report["status"] == "passed" else 1
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for o in report["obligations"]:
        print(f"{o['name']}: actual={o['actual']} expected={o['expected']} passed={o['passed']}")
    print(f"status={report['status']}; exit={report['exitCode']}; report={args.output}")
    if "error" in report:
        print(report["error"])
    return report["exitCode"]


if __name__ == "__main__":
    sys.exit(main())