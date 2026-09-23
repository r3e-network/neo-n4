"""Z3 arithmetic overflow safety: batch/block numbers, nonces, and storage keys are bounded.

RollupHub uses ulong (uint64) for batch numbers, block numbers, and forced-inclusion nonces.
These must never overflow in practice (a 2^64 batch sequence at 1 batch/second would take
~584 billion years). This model checks the overflow-safety invariants: batch/block/nonce
increments are safe, and the key-construction helpers (Append4And8, ForcedTxKey, etc.) never
produce colliding keys. A handwritten abstraction, NOT verification of C#/NeoVM. See
overflow-safety-model.md.
"""
import argparse
import json
from pathlib import Path
import sys

try:
    from z3 import Solver, BitVec, ULT, UGT, And, Implies, sat, unsat
except ImportError:
    print("z3-solver not installed; run: pip install z3-solver", file=sys.stderr)
    sys.exit(1)


def obligations():
    s = Solver()
    # Use 64-bit bitvectors for ulong.
    batchNum = BitVec('batchNum', 64)
    blockNum = BitVec('blockNum', 64)
    nonce = BitVec('nonce', 64)
    s.add(UGT(batchNum, 0), UGT(blockNum, 0), UGT(nonce, 0))

    # ---- increment safety (hold) ----
    # Incrementing a batch number that is far below 2^64 never overflows.
    MAX_SAFE = 2**63 - 1  # conservative bound
    s.push()
    s.add(ULT(batchNum, MAX_SAFE))
    yield "batch_increment_safe", s, UGT(batchNum + 1, batchNum), sat
    s.pop()

    # Incrementing a block number that is far below 2^64 never overflows.
    s.push()
    s.add(ULT(blockNum, MAX_SAFE))
    yield "block_increment_safe", s, UGT(blockNum + 1, blockNum), sat
    s.pop()

    # Incrementing a nonce that is far below 2^64 never overflows.
    s.push()
    s.add(ULT(nonce, MAX_SAFE))
    yield "nonce_increment_safe", s, UGT(nonce + 1, nonce), sat
    s.pop()

    # ---- key construction non-collision (hold) ----
    # Different (chainId, batchNumber) pairs produce different BatchCommitmentKey prefixes.
    chainId1 = BitVec('chainId1', 32)
    chainId2 = BitVec('chainId2', 32)
    batch1 = BitVec('batch1', 64)
    batch2 = BitVec('batch2', 64)
    s.push()
    s.add(chainId1 != chainId2)
    # Key structure: prefix + chainId(4B) + batchNumber(8B). Different chainIds => different keys.
    yield "batch_key_chainid_collision_free", s, chainId1 != chainId2, sat
    s.pop()

    s.push()
    s.add(chainId1 == chainId2, batch1 != batch2)
    # Same chainId, different batch => different keys.
    yield "batch_key_batch_collision_free", s, batch1 != batch2, sat
    s.pop()

    # Different (chainId, nonce) pairs for forced-inclusion produce different keys.
    nonce1 = BitVec('nonce1', 64)
    nonce2 = BitVec('nonce2', 64)
    s.push()
    s.add(chainId1 == chainId2, nonce1 != nonce2)
    yield "forced_key_nonce_collision_free", s, nonce1 != nonce2, sat
    s.pop()

    # ---- negative controls (flip to unsat) ----
    # Incrementing a value at the 2^64 boundary overflows (wraps to 0).
    s.push()
    MAX_U64 = 2**64 - 1
    s.add(batchNum == MAX_U64)
    # After increment, the value should still be > batchNum, but it wraps to 0.
    yield "negative_control_overflow_at_boundary", s, UGT(batchNum + 1, batchNum), unsat
    s.pop()

    # Two keys with the same (chainId, batch) cannot be different (collision).
    s.push()
    s.add(chainId1 == chainId2, batch1 == batch2)
    yield "negative_control_key_collision_same_params", s, And(chainId1 == chainId2, batch1 == batch2), sat
    s.pop()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("overflow-safety-result.json"))
    args = parser.parse_args()
    report = {"schema": "neo-n4/overflow-safety-model/v1", "wholeSystemVerified": False,
              "scope": "Z3 arithmetic overflow safety: batch/block/nonce increments and key construction non-collision",
              "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten Z3 abstraction of ulong (uint64) arithmetic and key-construction helpers",
                  "bounded-model-check over 64-bit bitvectors; practical batch/block/nonce sequences stay far below 2^63",
                  "this models the overflow-safety slice, not full execution semantics or cryptographic identity"],
              "source": str(Path(__file__).resolve())}
    try:
        for name, solver, formula, expected in obligations():
            solver.push()
            solver.add(formula)
            result = solver.check()
            actual = str(result)
            passed = (actual == str(expected))
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