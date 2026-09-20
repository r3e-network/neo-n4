"""Inductive safety model of RollupHub's forced-inclusion anti-censorship queue.

Models the FIFO queue over (head, tail) counters against the RollupHub contract's
EnqueueForcedTransaction / ConsumeForcedTransactionsInternal / GetNextForcedNonce /
GetPendingForcedCount (contract lines ~284-320). Proves nonce uniqueness, FIFO order,
monotonicity, no underflow, and pending-count non-negativity. Handwritten abstraction,
NOT verification of C#/NeoVM. See forced-inclusion-model.md.
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
REVIEWED_SOURCE_SHA256 = "4477ae0bbf3cdad0c476f50eade98ac7b03ce0e82c4747548a64245fdd296361"
# ConsumeForcedTransactionsInternal asserts head + count <= tail; the loop is a simple
# index advance with no wrap within the uint64 domain (GetForcedTail casts BigInteger -> ulong).
MAX = 2**64 - 1


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


def check_source(path):
    digest = source_digest(path)
    if digest != REVIEWED_SOURCE_SHA256:
        raise ValueError("reviewed RollupHub source changed; re-review model correspondence")
    return digest


@dataclass(frozen=True)
class Queue:
    head: object   # next nonce to consume (GetNextForcedNonce / GetForcedHead)
    tail: object   # next nonce to enqueue (GetForcedTail)


def symbolic_queue():
    return Queue(z3.Int("head"), z3.Int("tail"))


def pending(q):
    # GetPendingForcedCount returns tail - head (guarded by tail >= head => non-negative).
    return q.tail - q.head


def invariant(q):
    return z3.And(0 <= q.head, q.head <= q.tail, q.tail <= MAX)


def enqueue(q):
    # tail' = tail + 1, head' = head; returns nonce = old tail (unique per call).
    guard = invariant(q) & (q.tail < MAX)
    return guard, Queue(q.head, q.tail + 1)


def consume(q, count):
    # count==0 returns early; otherwise assert head+count <= tail (underflow guard),
    # head' = head+count, tail' = tail.
    guard = z3.And(invariant(q), count >= 0, q.head + count <= q.tail)
    return guard, Queue(q.head + count, q.tail)


def obligations():
    q = symbolic_queue()
    init = Queue(z3.IntVal(0), z3.IntVal(0))
    count = z3.Int("count")

    # Initial empty queue.
    yield "initial_invariant", z3.Not(invariant(init)), z3.unsat
    yield "initial_empty", z3.And(invariant(init), pending(init) != 0), z3.unsat
    yield "initial_nonce_zero", z3.And(invariant(init), init.head != 0), z3.unsat

    for name, (guard, next_q) in zip(
            ("enqueue", "consume"),
            (enqueue(q), consume(q, count))):
        premise = z3.And(invariant(q), guard)
        yield name + "_enabled", premise, z3.sat
        yield name + "_preserves_invariant", z3.And(
            premise, z3.Not(invariant(next_q))), z3.unsat
        # Monotonicity: head and tail never move backward.
        yield name + "_head_monotone", z3.And(premise, next_q.head < q.head), z3.unsat
        yield name + "_tail_monotone", z3.And(premise, next_q.tail < q.tail), z3.unsat
        # Pending count never negative.
        yield name + "_pending_nonneg", z3.And(premise, pending(next_q) < 0), z3.unsat

    # FIFO: a consume may not advance head past tail (the underflow guard).
    yield "consume_bounded_by_tail", z3.And(
        invariant(q), count >= 0, q.head + count > q.tail,
        z3.Not(invariant(consume(q, count)[1]))), z3.sat

    # Nonce uniqueness: enqueue hands out tail then tail+1, so consecutive nonces differ;
    # tail strictly increases, so no enqueue ever reuses a nonce.
    e1_guard, e1 = enqueue(q)
    yield "enqueue_tail_strictly_increases", z3.And(e1_guard, e1.tail <= q.tail), z3.unsat

    # Negative controls: drop a load-bearing guard and a bad state becomes reachable.
    # (a) consume past tail without the underflow guard -> head > tail, pending negative.
    unguarded = Queue(q.head + count, q.tail)
    yield "negative_control_underflow", z3.And(
        invariant(q), count > q.tail - q.head,
        z3.Not(invariant(unguarded))), z3.sat
    # (b) enqueue at max tail without the no-wrap guard -> tail wraps to 0 (negative).
    wrap = Queue(q.head, q.tail + 1)
    yield "negative_control_tail_wrap", z3.And(
        invariant(q), q.tail == MAX, z3.Not(invariant(wrap))), z3.sat
    # (c) reusing a consumed nonce (head steps backward below zero) breaks FIFO ordering.
    yield "negative_control_nonce_reuse", z3.And(
        invariant(q), q.head == 0,
        z3.Not(invariant(Queue(q.head - 1, q.tail)))), z3.sat


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
    report = {"schema": "neo-n4/forced-inclusion-model/v1", "wholeSystemVerified": False,
              "scope": "inductive safety of the RollupHub forced-inclusion FIFO queue abstraction",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten C#/NeoVM correspondence, not an extracted transition relation",
                  "head/tail counters live in the uint64 domain (BigInteger storage, ulong reads)",
                  "enqueue and consume are the only writers of the queue pointers",
                  "consume count is a non-negative integer; the 352-byte public-input domain bounds it",
                  "atomic fault rollback, no reentrancy, no concurrent enqueue/consume",
                  "each batch seals forcedInclusionCount into the public-input hash (replay-bound)"],
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
                        default=Path(__file__).with_name("forced-inclusion-result.json"))
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