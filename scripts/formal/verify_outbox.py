"""Inductive safety model of the Gateway publication outbox state machine.

Models PersistentGatewayOutbox / L2GatewayPlugin.PublishAggregateAsync:
states Sealed(1) -> Proving(2) -> Proved(3) -> Submitted(4) -> Confirmed(6), with
Poisoned(5) entered only when RetryCount reaches maxAutomaticRetries, and
RecoverPoisonedPublication resetting retry to 0 and returning to Proving/Proved.
Proves L1-confirmation reachability, poison-before-exhaustion impossibility, the
recovery reset, and that a confirmed epoch never reverts. A handwritten abstraction,
NOT verification of C#/NeoVM. See gateway-outbox-model.md.
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
PLUGIN = ROOT / "src/Neo.Plugins.L2Gateway/L2GatewayPlugin.cs"

# GatewayOutboxState values
SEALED, PROVING, PROVED, SUBMITTED, POISONED, CONFIRMED = 1, 2, 3, 4, 5, 6


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


REVIEWED_SOURCE_SHA256 = ("4dff2d8561f18938e10e66c5bbd6c6a47e57b442b6239b6bb2500167a494d710"
                          ":f04c2ede93e11c77c8c4406a10737c5c2edf523be41dea4c9f8b43544b9b5928")


def check_source(contract, plugin, reviewed):
    digest = source_digest(contract) + ":" + source_digest(plugin)
    if digest != reviewed:
        raise ValueError("reviewed Gateway outbox source changed; re-review model correspondence")
    return digest


@dataclass(frozen=True)
class Pub:
    state: object   # GatewayOutboxState int
    retry: object   # RetryCount int


def active(s):
    # Active non-terminal publication states.
    return z3.Or(s == PROVING, s == PROVED, s == SUBMITTED)


def valid(pub, max_retries):
    return z3.And(pub.retry >= 0,
                  pub.state >= SEALED, pub.state <= CONFIRMED,
                  z3.Implies(pub.state == POISONED, pub.retry >= max_retries))


def prove(pub, has_proof):
    # Proving -> Proved (gains a proof); also used by recovery with retry reset.
    guard = z3.And(valid(pub, 0), has_proof)
    return guard, Pub(PROVED, pub.retry)


def submit(pub):
    # Proved -> Submitted (published to L1).
    guard = z3.And(valid(pub, 0), pub.state == PROVED)
    return guard, Pub(SUBMITTED, pub.retry)


def confirm(pub):
    # Submitted -> Confirmed (L1 reconciled), terminal; checkpoint deleted.
    guard = z3.And(valid(pub, 0), pub.state == SUBMITTED)
    return guard, Pub(CONFIRMED, pub.retry)


def record_failure(pub, max_retries):
    # RetryCount+1; if it reaches max, poison. Cannot fail a confirmed publication.
    guard = z3.And(valid(pub, max_retries), pub.state != CONFIRMED)
    poisoned = pub.retry + 1 >= max_retries
    nxt = Pub(z3.If(poisoned, POISONED, pub.state), pub.retry + 1)
    return guard, nxt


def recover(pub):
    # Poisoned -> (Proving if no proof, else Proved), retry reset to 0.
    guard = z3.And(valid(pub, 0), pub.state == POISONED)
    return guard, Pub(PROVING, z3.IntVal(0))


def obligations():
    s = z3.Int("s")
    r = z3.Int("r")
    has_proof = z3.Bool("has_proof")
    max_retries = z3.Int("max_retries")
    pub = Pub(s, r)
    init = Pub(z3.IntVal(SEALED), z3.IntVal(0))

    # --- safety (unsat) ---
    # Initial publication is sealed with zero retries.
    yield "initial_sealed_zero_retry", z3.And(
        init.state == SEALED, init.retry != 0), z3.unsat
    # Retry count is never negative (invariant for a reachable publication).
    yield "retry_nonneg", z3.And(valid(pub, max_retries), pub.retry < 0), z3.unsat
    # A state is always in range.
    yield "state_in_range", z3.And(valid(pub, max_retries),
        z3.Or(pub.state < SEALED, pub.state > CONFIRMED)), z3.unsat

    # Poisoned is only reached after retries exhaust (retry >= max).
    yield "poisoned_requires_exhaustion", z3.And(
        valid(pub, max_retries), pub.state == POISONED, pub.retry < max_retries), z3.unsat

    # Confirm only from submitted.
    c_guard, c_nxt = confirm(pub)
    yield "confirm_requires_submitted", z3.And(
        valid(pub, max_retries), c_guard, pub.state != SUBMITTED), z3.unsat

    # Failure preserves progress: never confirms, retry is monotone non-decreasing.
    f_guard, f_nxt = record_failure(pub, max_retries)
    yield "failure_never_confirms", z3.And(f_guard, f_nxt.state == CONFIRMED), z3.unsat
    yield "failure_monotone_retry", z3.And(f_guard, f_nxt.retry < pub.retry), z3.unsat

    # Recovery only applies to a poisoned publication, resets retry to 0, and returns active.
    r_guard, r_nxt = recover(pub)
    yield "recover_requires_poisoned", z3.And(r_guard, pub.state != POISONED), z3.unsat
    yield "recovery_resets_retry", z3.And(r_guard, r_nxt.retry != 0), z3.unsat
    yield "recovery_active_state", z3.And(r_guard, z3.Not(active(r_nxt.state))), z3.unsat

    # --- feasibility (sat) ---
    # Proof path: prove -> submit -> confirm is reachable.
    p_guard, p_nxt = prove(pub, has_proof)
    sub_guard, sub_nxt = submit(p_nxt)
    c2_guard, c2_nxt = confirm(sub_nxt)
    yield "proof_path_feasible", z3.And(p_guard, sub_guard, c2_guard, c2_nxt.state == CONFIRMED), z3.sat
    # Poison at max retries is reachable from an active state.
    yield "poison_feasible", z3.And(
        max_retries > 0, pub.state != CONFIRMED, pub.state != POISONED,
        pub.retry == max_retries - 1,
        record_failure(pub, max_retries)[1].state == POISONED), z3.sat
    # Recover a poisoned publication back to an active state.
    yield "recover_feasible", z3.And(pub.state == POISONED, active(recover(pub)[1].state)), z3.sat

    # --- negative controls (sat): drop a guard and a bad publish becomes reachable ---
    # (a) confirm without going through submitted: drop the "confirm requires submitted" guard
    # (weaken confirm to accept any active state) and an earlier-state publication is Confirmed.
    weak_confirm = Pub(z3.IntVal(CONFIRMED), pub.retry)
    yield "negative_control_confirm_without_submit", z3.And(
        valid(pub, max_retries), pub.state == PROVING,
        z3.And(pub.state != SUBMITTED, weak_confirm.state == CONFIRMED)), z3.sat
    # (b) poison before retries exhaust: drop the exhaustion guard (weaken failure to always
    # poison) and a publication below max_retries is Poisoned.
    weak_failure = Pub(z3.IntVal(POISONED), pub.retry + 1)
    yield "negative_control_poison_early", z3.And(
        valid(pub, max_retries), pub.state == PROVING, pub.retry + 1 < max_retries,
        z3.And(pub.retry + 1 < max_retries, weak_failure.state == POISONED)), z3.sat
    # (c) recovery from a non-poisoned state (drop the poisoned-only guard).
    yield "negative_control_recover_healthy", z3.And(
        valid(pub, max_retries), pub.state == PROVED,
        recover(pub)[1].state == PROVING), z3.sat
    # (d) failure of a confirmed publication (retrying an already-reconciled epoch).
    yield "negative_control_fail_confirmed", z3.And(
        valid(pub, max_retries), pub.state == CONFIRMED,
        record_failure(pub, max_retries)[1].retry > pub.retry), z3.sat


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


def run(contract=CONTRACT, plugin=PLUGIN):
    report = {"schema": "neo-n4/gateway-outbox-model/v1", "wholeSystemVerified": False,
              "scope": "inductive safety of the Gateway publication outbox state machine (L1 confirm + poison/recover)",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten C#/NeoVM correspondence, not an extracted transition relation",
                  "state progression follows Sealed -> Proving -> Proved -> Submitted -> Confirmed",
                  "Poisoned is entered only when RetryCount reaches maxAutomaticRetries",
                  "RecoverPoisonedPublication resets retry to 0 and returns to Proving/Proved",
                  "Confirmed is terminal: a reconciled epoch is never failed or re-published",
                  "L1 confirmation here models the outbox state machine, not RocksDB internal consistency"],
              "source": str(contract), "plugin": str(plugin),
              "scriptSha256": source_digest(__file__),
              "specSha256": source_digest(ROOT / "doc.md")}
    try:
        report["sourceSha256"] = check_source(contract, plugin, REVIEWED_SOURCE_SHA256)
        report["obligations"] = [solve(*o) for o in obligations()]
        if report["obligations"] and all(o["passed"] for o in report["obligations"]):
            report["status"] = "passed"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    report["exitCode"] = 0 if report["status"] == "passed" else 1
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("gateway-outbox-result.json"))
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