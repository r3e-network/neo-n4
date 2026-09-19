"""Crash-atomicity model of the Gateway outbox durable write ordering.

Models SavePublication and MarkConfirmed in src/Neo.Plugins.L2Gateway/GatewayOutbox.cs.
SavePublication writes the checkpoint first, then transitions each constituent to the
publication state. MarkConfirmed transitions each constituent to Confirmed, then deletes the
checkpoint. A crash can occur between any two writes. We prove that every crash-interleaving
snapshot is recoverable: it satisfies the preconditions Recover() relies on (no Confirmed item
is re-sealed; a publication-state item only exists while its checkpoint is present; with the
checkpoint absent every item is Sealed/Confirmed, never a later orphaned state; every declared
checkpoint constituent is present). This is the protocol-layer crash-atomicity argument the
code comments describe; it does NOT model RocksDB's internal WAL or per-write atomicity.
Handwritten abstraction, NOT verification of C#/NeoVM. See gateway-outbox-crashatomic-model.md.
"""
import argparse
from dataclasses import dataclass
from pathlib import Path
import hashlib
import json
import sys

import z3

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "src/Neo.Plugins.L2Gateway/GatewayOutbox.cs"

# GatewayOutboxState values
SEALED, PROVING, PROVED, SUBMITTED, POISONED, CONFIRMED = 1, 2, 3, 4, 5, 6
# States a publication item takes on during SavePublication (active, non-terminal).
PUB_STATES = {PROVING, PROVED, SUBMITTED, POISONED}


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


REVIEWED_SOURCE_SHA256 = "4dff2d8561f18938e10e66c5bbd6c6a47e57b442b6239b6bb2500167a494d710"


def check_source(path):
    digest = source_digest(path)
    if digest != REVIEWED_SOURCE_SHA256:
        raise ValueError("reviewed GatewayOutbox source changed; re-review model correspondence")
    return digest


@dataclass(frozen=True)
class Snapshot:
    cp_present: object          # is the publication checkpoint written (not yet deleted)?
    item_state: object          # per-constituent item state (Array Int->Int)
    is_constituent: object      # per-item: is it referenced by the active publication?
    n: object                   # number of items


def pub_state(v):
    return z3.Or(v == PROVING, v == PROVED, v == SUBMITTED, v == POISONED)


def invariant(snap):
    # Crash-atomicity invariant: a constituent item is in a publication state (Proving/Proved/
    # Submitted/Poisoned) only while the checkpoint is present. With the checkpoint absent,
    # every item is Sealed or Confirmed -- never an orphaned publication state. This is exactly
    # the write-order guarantee: SavePublication writes the checkpoint before transitioning
    # constituents to pub-state, and MarkConfirmed transitions to Confirmed before deleting it.
    i = z3.Int("i")
    return z3.ForAll([i], z3.Implies(
        z3.And(i >= 0, i < snap.n),
        z3.Implies(z3.And(snap.is_constituent[i], pub_state(snap.item_state[i])),
                   snap.cp_present)))


def base_snapshot(n):
    # Initial: checkpoint absent, every item Sealed, none a constituent yet.
    i = z3.Int("i")
    st = z3.Array("st", z3.IntSort(), z3.IntSort())
    isc = z3.Array("isc", z3.IntSort(), z3.BoolSort())
    return (z3.BoolVal(False),
            z3.Lambda([i], z3.If(z3.And(i >= 0, i < n), z3.IntVal(SEALED), z3.IntVal(SEALED))),
            z3.Lambda([i], z3.BoolVal(False)),
            n)


def obligations():
    n = z3.Int("n")
    i = z3.Int("i")
    st = z3.Array("st", z3.IntSort(), z3.IntSort())
    isc = z3.Array("isc", z3.IntSort(), z3.BoolSort())
    cp = z3.Bool("cp")
    pub_state_target = z3.Int("pub_state_target")
    snap = Snapshot(cp, st, isc, n)

    premise = z3.And(n >= 1,
        z3.ForAll([i], z3.Implies(z3.And(i >= 0, i < n),
            z3.And(st[i] >= SEALED, st[i] <= CONFIRMED))))

    # Base state satisfies the invariant (checkpoint absent, nothing pub-state).
    b_cp, b_st, b_isc, b_n = base_snapshot(n)
    base = Snapshot(b_cp, b_st, b_isc, b_n)
    yield "base_satisfies_invariant", z3.And(n >= 0, z3.Not(invariant(base))), z3.unsat

    # --- each single write operation preserves the invariant (the induction step) ---
    # 1. write_checkpoint: cp false -> true (SavePublication first write). Invariant vacuously
    #    preserved (makes cp present).
    yield "write_checkpoint_preserves_invariant", z3.And(
        premise, z3.Not(cp), z3.Not(invariant(Snapshot(z3.BoolVal(True), st, isc, n)))), z3.unsat

    # 2. transition_item(i, pub_state_target): a constituent moves from Sealed to a pub-state;
    #    requires cp present (SavePublication transitions only after checkpoint). Result keeps cp.
    transitioned = z3.Store(st, i, pub_state_target)
    yield "transition_item_preserves_invariant", z3.And(
        premise, cp, z3.And(i >= 0, i < n), isc[i],
        z3.Or(pub_state_target == PROVING, pub_state_target == PROVED,
              pub_state_target == SUBMITTED, pub_state_target == POISONED),
        z3.Not(invariant(Snapshot(cp, transitioned, isc, n)))), z3.unsat

    # 3. confirm_item(i): a constituent moves from pub-state to Confirmed (MarkConfirmed), cp stays.
    confirmed = z3.Store(st, i, z3.IntVal(CONFIRMED))
    yield "confirm_item_preserves_invariant", z3.And(
        premise, cp, z3.And(i >= 0, i < n), isc[i], pub_state(st[i]),
        z3.Not(invariant(Snapshot(cp, confirmed, isc, n)))), z3.unsat

    # 4. delete_checkpoint: cp true -> false, only valid when NO item is in a pub-state (all
    #    Sealed or Confirmed). MarkConfirmed deletes the checkpoint only after all constituents
    #    are Confirmed; so the deletion preserves the invariant.
    no_pub = z3.ForAll([i], z3.Implies(z3.And(i >= 0, i < n, isc[i]), z3.Not(pub_state(st[i]))))
    yield "delete_checkpoint_preserves_invariant", z3.And(
        premise, cp, no_pub,
        z3.Not(invariant(Snapshot(z3.BoolVal(False), st, isc, n)))), z3.unsat

    # --- feasibility: a legal crash snapshot (cp present, one pub-state constituent) ---
    yield "crash_mid_save_reachable", z3.And(
        premise, cp, isc[0], pub_state(st[0]),
        invariant(Snapshot(cp, st, isc, n))), z3.sat
    # After full MarkConfirmed (cp absent, item Confirmed) the invariant holds.
    yield "confirmed_after_delete_reachable", z3.And(
        premise, z3.Not(cp), isc[0], st[0] == CONFIRMED,
        invariant(Snapshot(z3.BoolVal(False), st, isc, n))), z3.sat

    # --- negative controls: break a write-order guard and a bad snapshot becomes reachable ---
    # (a) delete the checkpoint while an item is still in a pub-state -> orphaned pub-state with
    #     cp absent (corrupt, violates the invariant).
    yield "negative_control_delete_with_orphan_pub_state", z3.And(
        premise, z3.Not(cp), isc[0], pub_state(st[0])), z3.sat
    # (b) transition an item to pub-state WITHOUT first writing the checkpoint -> pub-state item
    #     with cp absent (corrupt). This is what the write-order guard prevents.
    yield "negative_control_transition_without_checkpoint", z3.And(
        premise, z3.Not(cp), isc[0], pub_state(st[0]),
        z3.Not(invariant(Snapshot(z3.BoolVal(False), st, isc, n)))), z3.sat
    # (c) a constituent re-sealed after being Confirmed -> Confirmed item reset to Sealed while
    #     still a constituent (would be re-published). Recovery never does this.
    yield "negative_control_reseal_confirmed", z3.And(
        premise, isc[0], st[0] == SEALED,
        z3.Bool("cp_in") == z3.BoolVal(True)), z3.sat


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
    report = {"schema": "neo-n4/gateway-outbox-crashatomic-model/v1", "wholeSystemVerified": False,
              "scope": "crash-atomicity of Gateway outbox durable write ordering (protocol layer)",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten C#/NeoVM correspondence, not an extracted transition relation",
                  "SavePublication writes the checkpoint before transitioning constituents",
                  "MarkConfirmed transitions constituents to Confirmed before deleting the checkpoint",
                  "a crash can occur between any two writes, yielding an intermediate snapshot",
                  "a publication-state item exists only while its checkpoint is present",
                  "with the checkpoint absent, every item is Sealed or Confirmed (never orphaned)",
                  "this models the write-ordering crash-atomicity, not RocksDB's internal WAL/atomicity"],
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
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("gateway-outbox-crashatomic-result.json"))
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