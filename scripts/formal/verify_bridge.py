"""Inductive safety model of SharedBridge asset-escrow conservation.

Models the per-(chainId, asset) locked-balance accounting in
NeoHub.SharedBridgeContract: Deposit credits the escrow after transferring the
asset in (line 294), FinalizeWithdrawal* debits it under the
`currentBal >= amount` guard after a verified, once-only consumed withdrawal leaf
(ConsumeAndPayout line 539-550, Credit/DebitLockedBalance 556-575). Proves the
escrow never goes negative and payouts never exceed deposits (conservation).
Handwritten abstraction, NOT verification of C#/NeoVM. See bridge-conservation-model.md.
"""
import argparse
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import sys

import z3

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "contracts/NeoHub.SharedBridge/SharedBridgeContract.cs"
REVIEWED_SOURCE_SHA256 = "906945c4115aad7885436862b7dbfbff01c17e20bad7436defd22004062e1b85"


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


def check_source(path):
    digest = source_digest(path)
    if digest != REVIEWED_SOURCE_SHA256:
        raise ValueError("reviewed SharedBridge source changed; re-review model correspondence")
    return digest


@dataclass(frozen=True)
class Escrow:
    locked: object   # per-(chain,asset) escrowed balance (BigInteger storage)
    deposited: object  # cumulative deposits credited (minted into escrow)
    paid: object       # cumulative payouts debited (paid out on L1)


def symbolic():
    return Escrow(z3.Int("locked"), z3.Int("deposited"), z3.Int("paid"))


def invariant(e):
    # Escrow never negative; it equals cumulative deposits minus cumulative payouts;
    # payouts never exceed deposits (no over-payout / conservation).
    return z3.And(e.locked >= 0, e.locked == e.deposited - e.paid,
                  e.deposited >= 0, e.paid >= 0, e.deposited >= e.paid)


def deposit(e, amount):
    # Deposit(asset, amount, targetChainId, ...): amount>0, asset transferred in, then credit.
    guard = z3.And(invariant(e), amount > 0)
    nxt = Escrow(e.locked + amount, e.deposited + amount, e.paid)
    return guard, nxt


def withdraw(e, amount):
    # FinalizeWithdrawal*: debit under currentBal >= amount guard, then payout.
    guard = z3.And(invariant(e), amount > 0, e.locked >= amount)
    nxt = Escrow(e.locked - amount, e.deposited, e.paid + amount)
    return guard, nxt


def obligations():
    e = symbolic()
    amount = z3.Int("amount")
    init = Escrow(z3.IntVal(0), z3.IntVal(0), z3.IntVal(0))

    yield "initial_invariant", z3.Not(invariant(init)), z3.unsat
    yield "initial_empty", z3.And(invariant(init), init.locked != 0), z3.unsat

    for name, (guard, nxt) in zip(("deposit", "withdraw"), (deposit(e, amount), withdraw(e, amount))):
        premise = z3.And(invariant(e), guard)
        yield name + "_enabled", premise, z3.sat
        yield name + "_preserves_invariant", z3.And(
            premise, z3.Not(invariant(nxt))), z3.unsat
        # Conservation: paid never exceeds deposited (no minting out of nothing).
        yield name + "_payout_leq_deposited", z3.And(
            premise, nxt.paid > nxt.deposited), z3.unsat
        # Locked never goes negative.
        yield name + "_locked_nonneg", z3.And(premise, nxt.locked < 0), z3.unsat

    # A withdraw exactly equal to escrow drains it to zero (valid).
    yield "withdraw_drains_to_zero", z3.And(
        invariant(e), amount > 0, e.locked == amount,
        z3.And(invariant(withdraw(e, amount)[1]))), z3.sat

    # Negative controls: drop a load-bearing guard and a bad state becomes reachable.
    # (a) withdraw past escrow without the balance guard -> negative escrow (over-payout).
    unguarded = Escrow(e.locked - amount, e.deposited, e.paid + amount)
    yield "negative_control_over_payout", z3.And(
        invariant(e), amount > 0, e.locked < amount,
        z3.Not(invariant(unguarded))), z3.sat
    # (b) two payouts for one deposit without replay protection -> paid > deposited.
    double = Escrow(e.locked - 2 * amount, e.deposited - amount, e.paid + 2 * amount)
    yield "negative_control_double_payout", z3.And(
        invariant(e), amount > 0, e.deposited == amount, e.paid == 0,
        z3.Not(invariant(double))), z3.sat
    # (c) credit escrow without the corresponding deposit credit (mint out of nothing):
    # locked increases but deposited does not, breaking conservation locked==deposited-paid.
    mint = Escrow(e.locked + amount, e.deposited, e.paid)
    yield "negative_control_credit_without_deposit", z3.And(
        invariant(e), amount > 0, z3.Not(invariant(mint))), z3.sat


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
    report = {"schema": "neo-n4/bridge-conservation-model/v1", "wholeSystemVerified": False,
              "scope": "inductive safety of SharedBridge per-(chain,asset) escrow accounting",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten C#/NeoVM correspondence, not an extracted transition relation",
                  "deposit transfers the asset in and credits; withdraw debits and pays out",
                  "withdrawal replay protection: a consumed withdrawal leaf is paid at most once",
                  "per-(chainId,asset) escrow is the only balance mutated by deposit/withdraw",
                  "atomic fault rollback, no reentrancy, no concurrent deposit/withdraw",
                  "bridge conservation here covers L1 escrow accounting, not L2 mint/burn or GAS"],
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
                        default=Path(__file__).with_name("bridge-conservation-result.json"))
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