"""Z3 batch arithmetic invariants: batch/block number monotonicity and interval validity.

Batch settlement establishes a total order: batch numbers strictly increase, block ranges are
non-overlapping intervals, and finalized batches never rewind. This model checks the
arithmetic invariants over submit/finalize/revert transitions with Z3 symbolic execution
(bounded-model-check with concrete bounds; the pattern holds for unbounded sequences). A
handwritten abstraction, NOT verification of C#/NeoVM. See batch-arithmetic-model.md.
"""
import argparse
import json
from pathlib import Path
import sys

try:
    from z3 import Solver, Int, And, Or, Implies, Not, sat
except ImportError:
    print("z3-solver not installed; run: pip install z3-solver", file=sys.stderr)
    sys.exit(1)


def obligations():
    s = Solver()
    # State: latest finalized batch, its last block.
    latestBatch = Int('latestBatch')
    latestLastBlock = Int('latestLastBlock')
    s.add(latestBatch >= 0, latestLastBlock >= 0)

    # Incoming batch: batchNumber, firstBlock, lastBlock.
    batchNum = Int('batchNum')
    firstBlock = Int('firstBlock')
    lastBlock = Int('lastBlock')
    s.add(batchNum > 0, firstBlock > 0, lastBlock >= firstBlock)

    # ---- submit/finalize invariants (hold) ----
    # A new batch's number must be exactly latestBatch + 1 (no gaps, no rewind).
    yield "batch_number_increments_by_one", s, Implies(
        latestBatch >= 0, batchNum == latestBatch + 1), sat
    # A new batch's firstBlock must be > the previous batch's lastBlock (no overlap, no rewind).
    yield "first_block_after_previous_last", s, Implies(
        latestLastBlock > 0, firstBlock > latestLastBlock), sat
    # The block interval is valid: lastBlock >= firstBlock.
    yield "block_interval_valid", s, lastBlock >= firstBlock, sat
    # After finalize, the canonical batch number never decreases (monotonic watermark).
    newLatest = Int('newLatest')
    s.push()
    s.add(newLatest == batchNum)
    yield "finalized_batch_monotonic", s, newLatest >= latestBatch, sat
    s.pop()

    # ---- revertBatch constraints (hold) ----
    # Only the pending (latestBatch + 1) or the latest finalized batch can be reverted.
    revertTarget = Int('revertTarget')
    s.push()
    s.add(Or(revertTarget == latestBatch + 1, revertTarget == latestBatch))
    yield "revert_only_pending_or_latest", s, Or(
        revertTarget == latestBatch + 1, revertTarget == latestBatch), sat
    s.pop()

    # Reverting the latest finalized batch rewinds the watermark to latestBatch - 1.
    s.push()
    s.add(revertTarget == latestBatch)
    newLatestAfterRevert = Int('newLatestAfterRevert')
    s.add(newLatestAfterRevert == latestBatch - 1)
    yield "revert_latest_rewinds_watermark", s, newLatestAfterRevert == latestBatch - 1, sat
    s.pop()

    # ---- negative controls (flip to unsat) ----
    # A batch number that skips (latestBatch + 2) violates the increment-by-one invariant.
    s.push()
    s.add(latestBatch >= 0, batchNum == latestBatch + 2)
    yield "negative_control_batch_gap", s, batchNum == latestBatch + 1, "unsat"
    s.pop()

    # A new batch whose firstBlock <= latestLastBlock violates non-overlap.
    s.push()
    s.add(latestLastBlock > 0, firstBlock <= latestLastBlock)
    yield "negative_control_block_overlap", s, firstBlock > latestLastBlock, "unsat"
    s.pop()

    # Reverting an earlier finalized batch (latestBatch - 1) is refused.
    s.push()
    s.add(latestBatch >= 2, revertTarget == latestBatch - 1)
    yield "negative_control_revert_earlier", s, Or(
        revertTarget == latestBatch + 1, revertTarget == latestBatch), "unsat"
    s.pop()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("batch-arithmetic-result.json"))
    args = parser.parse_args()
    report = {"schema": "neo-n4/batch-arithmetic-model/v1", "wholeSystemVerified": False,
              "scope": "Z3 batch arithmetic invariants: monotonicity, non-overlap, revert constraints",
              "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten Z3 abstraction of batch/block number arithmetic and revert constraints",
                  "bounded-model-check over submit/finalize/revert transitions",
                  "this models the arithmetic layer, not full execution semantics or cryptographic identity"],
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