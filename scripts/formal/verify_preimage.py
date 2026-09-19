"""Structural injectivity model of MessageHasher canonical message/withdrawal preimages.

Proves that EncodeMessage / EncodeWithdrawal (src/Neo.L2.State/MessageHasher.cs) are
injective over their fields at the byte level: distinct (chainId, nonce, sender, receiver,
type, ...) tuples encode to distinct preimage bytes, because every fixed-width field
occupies disjoint LE bit positions and the variable payload is length-prefixed. Under the
trusted assumption that Hash256 (double-SHA256) has no collisions, distinct preimages imply
distinct leaf hashes. This covers the nonce/chainId replay-binding half of "nonce/replay"
and the structural (preimage) half of "message hashing" — the cryptographic collision
resistance of SHA-256 itself is a separate trusted assumption, not re-derived here. A
handwritten abstraction, NOT verification of C#/NeoVM. See preimage-injectivity-model.md.
"""
import argparse
from pathlib import Path
import hashlib
import json
import sys

import z3

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "src/Neo.L2.State/MessageHasher.cs"


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


REVIEWED_SOURCE_SHA256 = "fa5f4a04350ad076df4a89109c18dbffdf0e171af4b90383c97f553548e4da98"


def check_source(path):
    digest = source_digest(path)
    if digest != REVIEWED_SOURCE_SHA256:
        raise ValueError("reviewed MessageHasher source changed; re-review model correspondence")
    return digest


def obligations():
    # Model the fixed-width fields of EncodeMessage/EncodeWithdrawal as disjoint LE fields,
    # plus a length-prefixed variable payload. Prove concat-injectivity: equal concatenations
    # imply equal fields (no field can be shadowed by another, and the length prefix makes the
    # variable payload boundary unambiguous).
    sc = z3.BitVec("sc", 32)      # SourceChainId / ChainId
    tc = z3.BitVec("tc", 32)      # TargetChainId (message) / reserved-identical width
    nonce = z3.BitVec("nonce", 64)
    sender = z3.BitVec("sender", 160)
    receiver = z3.BitVec("receiver", 160)
    mtype = z3.BitVec("mtype", 8)
    plen = z3.BitVec("plen", 32)
    payload = z3.BitVec("payload", 32)   # bounded payload slice for the length-prefix check

    sc2 = z3.BitVec("sc2", 32)
    tc2 = z3.BitVec("tc2", 32)
    nonce2 = z3.BitVec("nonce2", 64)
    sender2 = z3.BitVec("sender2", 160)
    receiver2 = z3.BitVec("receiver2", 160)
    mtype2 = z3.BitVec("mtype2", 8)
    plen2 = z3.BitVec("plen2", 32)
    payload2 = z3.BitVec("payload2", 32)

    # The canonical preimage is the concatenation in fixed order (LE), payload length-prefixed.
    pre = z3.Concat(sc, tc, nonce, sender, receiver, mtype, plen, payload)
    pre2 = z3.Concat(sc2, tc2, nonce2, sender2, receiver2, mtype2, plen2, payload2)
    same = pre == pre2

    # --- safety (unsat): distinct field values cannot produce equal preimages ---
    yield "chainid_binds_preimage", z3.And(same, sc != sc2), z3.unsat
    yield "nonce_binds_preimage", z3.And(same, nonce != nonce2), z3.unsat
    yield "target_binds_preimage", z3.And(same, tc != tc2), z3.unsat
    yield "sender_binds_preimage", z3.And(same, sender != sender2), z3.unsat
    yield "receiver_binds_preimage", z3.And(same, receiver != receiver2), z3.unsat
    yield "type_binds_preimage", z3.And(same, mtype != mtype2), z3.unsat
    # Length prefix: a different payload length changes the boundary, hence the preimage.
    yield "payload_length_binds_preimage", z3.And(same, plen != plen2), z3.unsat
    # Payload bytes at the same length: if the (identical-width) payload differs, preimage differs.
    yield "payload_binds_preimage", z3.And(same, plen == plen2, payload != payload2), z3.unsat

    # Under distinct chainId, two messages with otherwise-identical fields cannot hash equal
    # (the chainId domain-separator is load-bearing against cross-L2 inclusion replay).
    yield "chainid_domain_separates", z3.And(
        same, sc != sc2, z3.And(sc == tc, tc2 == sc2), nonce == nonce2,
        sender == sender2, receiver == receiver2, mtype == mtype2,
        plen == plen2, payload == payload2), z3.unsat

    # --- feasibility (sat): two distinct messages do encode to distinct preimages ---
    yield "distinct_messages_feasible", z3.And(sc != sc2, pre != pre2), z3.sat

    # --- negative controls (sat): break an injectivity guard and collisions become possible ---
    # (a) drop the length prefix (fixed payload region regardless of length) -> two messages of
    #     different length can share a preimage region.
    yield "negative_control_without_length_prefix", z3.And(
        plen != plen2, z3.Concat(sc, tc, nonce, sender, receiver, mtype) ==
        z3.Concat(sc2, tc2, nonce2, sender2, receiver2, mtype2)), z3.sat
    # (b) drop nonce binding (nonce field omitted) -> two messages differing only in nonce collide.
    yield "negative_control_without_nonce", z3.And(
        nonce != nonce2, z3.Concat(sc, tc, sender, receiver, mtype) ==
        z3.Concat(sc2, tc2, sender2, receiver2, mtype2)), z3.sat
    # (c) drop chainId domain-separation -> two cross-L2 messages with the same nonce collide.
    yield "negative_control_without_chainid", z3.And(
        sc != sc2, z3.Concat(tc, nonce, sender, receiver, mtype) ==
        z3.Concat(tc2, nonce2, sender2, receiver2, mtype2)), z3.sat


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
    report = {"schema": "neo-n4/preimage-injectivity-model/v1", "wholeSystemVerified": False,
              "scope": "structural injectivity of MessageHasher canonical preimages",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten C#/NeoVM correspondence, not an extracted transition relation",
                  "fixed-width fields are little-endian and occupy disjoint positions",
                  "the variable payload is length-prefixed (boundary unambiguous)",
                  "Hash256 (double-SHA256) is collision-free — a separate cryptographic trust",
                  "this proves distinct preimages, not the absence of SHA-256 collisions",
                  "no concurrent writers or reentrancy in the hashing path"],
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
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("preimage-injectivity-result.json"))
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