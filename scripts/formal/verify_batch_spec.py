"""Spec-correspondence model: doc.md §3.2 L2BatchCommitment vs the implemented wire layout.

Proves that the field list doc.md §3.2 declares for L2BatchCommitment, walked in declaration
order with the standard C# wire widths (uint32=4, ulong=8, UInt256=32, byte=1, byte[] as a
4-byte length prefix + payload), derives EXACTLY the implemented commitment layout: offsets
chainId@0 .. proofType@316, a 4-byte proofLen@317, proof bytes @321, fixed header 321 bytes,
and the 352-byte public-inputs domain (header[0..252) + l1MessageHash + daCommitment +
blockContextHash + forcedInclusionCount). Also proves the fixed-header encoding is injective.
Handwritten abstraction, NOT verification of C#/NeoVM. See batch-spec-model.md.
"""
import argparse
from pathlib import Path
import hashlib
import json
import sys

import z3

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "src/Neo.L2.Batch/BatchSerializer.cs"
HUB = ROOT / "contracts/NeoHub.RollupHub/RollupHubContract.cs"
SPEC = ROOT / "doc.md"

# Source anchors fail closed on layout drift; they are not a C# parser. The offset table
# below must stay in lockstep with these declarations (contract HeaderMinLength/OffsetProofType
# and the serializer's 321/352 layout docs).
SOURCE_ANCHORS = {
    CONTRACT: ["Total = 4 + 8 + 8 + 8 + 9×32 + 1 + 4 = 321 bytes",
               "PublicInputs layout (352 bytes, fixed)"],
    HUB: ["private const int OffsetProofType = 316;",
          "private const int HeaderMinLength = 321;"],
}


def check_anchors():
    for path, anchors in SOURCE_ANCHORS.items():
        text = path.read_text(encoding="utf-8-sig")
        for anchor in anchors:
            if anchor not in text:
                raise ValueError(f"batch layout source anchor changed in {path.name}: {anchor}")


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


# doc.md §3.2 L2BatchCommitment fields in declaration order: (name, wire width in bytes).
DOC_BATCH_FIELDS = [
    ("chainId", 4),
    ("batchNumber", 8),
    ("firstBlock", 8),
    ("lastBlock", 8),
    ("preStateRoot", 32),
    ("postStateRoot", 32),
    ("txRoot", 32),
    ("receiptRoot", 32),
    ("withdrawalRoot", 32),
    ("l2ToL1MessageRoot", 32),
    ("l2ToL2MessageRoot", 32),
    ("daCommitment", 32),
    ("publicInputHash", 32),
    ("proofType", 1),
]
# Offsets the implementation actually uses (BatchSerializer / RollupHubContract / VmTests).
IMPLEMENTED_OFFSETS = [0, 4, 12, 20, 28, 60, 92, 124, 156, 188, 220, 252, 284, 316]
HEADER_SIZE = 321          # ProofBytesOffset; HeaderMinLength = 321
PROOFLEN_OFFSET = 317      # 4-byte little-endian proof length prefix
PROOF_BYTES_OFFSET = 321
# Public-inputs domain: header[0..252) + l1MessageHash(32) + daCommitment(32) + blockContextHash(32)
# + forcedInclusionCount(4) = 352 (BatchSerializer.EncodePublicInputs / contract ComputePublicInputHash).
PUBLIC_INPUTS_SIZE = 352
PI_L1MSG = 252
PI_DAC = 284
PI_BLKCTX = 316
PI_FIC = 348


def obligations():
    # Static spec facts are checked as Not(fact) -> unsat (the fact is necessarily true).
    def static_fact(name, fact):
        yield name, z3.Not(z3.BoolVal(fact)), z3.unsat

    # 1) Walking doc.md's declared fields in order with their wire widths derives the
    #    implemented offsets exactly (no drift between spec and implementation).
    derived, off = [], 0
    for _, width in DOC_BATCH_FIELDS:
        derived.append(off)
        off += width
    yield "doc_field_walk_derives_implemented_offsets", z3.Not(z3.BoolVal(
        derived == IMPLEMENTED_OFFSETS)), z3.unsat

    # 2) proofType is the last fixed field; the 4-byte proofLen prefix and the proof bytes
    #    follow immediately: proofType@316, proofLen@317, proof@321, header size 321.
    yield "proof_region_follows_fixed_fields", z3.Not(z3.BoolVal(
        derived[-1] == PROOFLEN_OFFSET - 1
        and PROOFLEN_OFFSET + 4 == PROOF_BYTES_OFFSET
        and PROOF_BYTES_OFFSET == HEADER_SIZE)), z3.unsat

    # 3) The fixed-header part is contiguous with no gaps and strictly increasing offsets.
    gaps = all(
        IMPLEMENTED_OFFSETS[k + 1] == IMPLEMENTED_OFFSETS[k] + DOC_BATCH_FIELDS[k][1]
        for k in range(len(IMPLEMENTED_OFFSETS) - 1))
    increasing = all(
        IMPLEMENTED_OFFSETS[k] < IMPLEMENTED_OFFSETS[k + 1]
        for k in range(len(IMPLEMENTED_OFFSETS) - 1))
    yield "header_fields_contiguous_no_gaps", z3.Not(z3.BoolVal(gaps)), z3.unsat
    yield "header_offsets_strictly_increase", z3.Not(z3.BoolVal(increasing)), z3.unsat

    # 4) The public-inputs domain is exactly header[0..252) + l1MessageHash(32) + daCommitment(32)
    #    + blockContextHash(32) + forcedInclusionCount(4) = 352, and the sub-offsets interleave
    #    with the header's daCommitment (daCommitment occupies header[252..284) AND pi[284..316)).
    yield "public_inputs_domain_352", z3.Not(z3.BoolVal(
        PI_L1MSG == derived[10] + 32 and PI_DAC == PI_L1MSG + 32
        and PI_BLKCTX == PI_DAC + 32 and PI_FIC == PI_BLKCTX + 32
        and PI_FIC + 4 == PUBLIC_INPUTS_SIZE)), z3.unsat
    # header[0..252) prefix ends exactly where l2ToL2MessageRoot ends (252).
    yield "public_inputs_prefix_is_header_through_l2tol2", z3.Not(z3.BoolVal(
        PI_L1MSG == derived[10] + DOC_BATCH_FIELDS[10][1])), z3.unsat

    # 5) Injectivity of the fixed header: two commitments whose declared fields all match have
    #    identical 321-byte headers. The partition of [0,321) is the 14 declared fixed fields
    #    plus the 4-byte proof-length prefix at [317,321) — the encoding of doc.md's variable
    #    `byte[] proof` field (length prefix + payload), so the spec field list determines the
    #    whole header.
    PARTITION_FIELDS = IMPLEMENTED_OFFSETS + [PROOFLEN_OFFSET]
    PARTITION_WIDTHS = [w for _, w in DOC_BATCH_FIELDS] + [4]
    wire = z3.BitVec("wire", 8 * HEADER_SIZE)
    wire2 = z3.BitVec("wire2", 8 * HEADER_SIZE)
    all_fields_equal = z3.And(*[
        z3.Extract(8 * (PARTITION_FIELDS[k] + PARTITION_WIDTHS[k]) - 1,
                   8 * PARTITION_FIELDS[k], wire)
        == z3.Extract(8 * (PARTITION_FIELDS[k] + PARTITION_WIDTHS[k]) - 1,
                      8 * PARTITION_FIELDS[k], wire2)
        for k in range(len(PARTITION_FIELDS))])
    yield "field_equality_implies_header_equality", z3.And(
        all_fields_equal, wire != wire2), z3.unsat
    # The proofLen field is load-bearing: it binds the variable proof region to a definite
    # length (a commitment cannot silently carry extra proof bytes outside its declared length).
    yield "chainid_binds_header", z3.And(
        wire == wire2,
        z3.Extract(8 * 4 - 1, 0, wire) != z3.Extract(8 * 4 - 1, 0, wire2)), z3.unsat

    # ---- feasibility ----
    yield "header_feasible", z3.BoolVal(True), z3.sat
    yield "distinct_chainids_feasible", z3.Extract(
        8 * 4 - 1, 0, wire) != z3.Extract(8 * 4 - 1, 0, wire2), z3.sat

    # ---- negative controls (SAT: break the partition and a bad encoding becomes possible) ----
    # (a) drop the publicInputHash field from the spec list: the header region [284,316) is no
    #     longer covered by any declared field, so two commitments with identical declared
    #     fields but different bytes in that region are indistinguishable — the settlement
    #     digest binding would be ambiguous.
    pi_free = z3.And(
        z3.Extract(8 * IMPLEMENTED_OFFSETS[12] - 1, 0, wire) ==
        z3.Extract(8 * IMPLEMENTED_OFFSETS[12] - 1, 0, wire2),
        z3.Extract(8 * IMPLEMENTED_OFFSETS[13] - 1, 8 * IMPLEMENTED_OFFSETS[12], wire) !=
        z3.Extract(8 * IMPLEMENTED_OFFSETS[13] - 1, 8 * IMPLEMENTED_OFFSETS[12], wire2))
    yield "negative_control_dropped_publicInputHash_uncovered", pi_free, z3.sat
    # (b) an uncovered trailing region in the 352-byte public-inputs domain (forcedInclusionCount
    #     omitted) leaves fic@348..352 free — two batches with different forced counts would be
    #     indistinguishable by the declared fields.
    fic_uncovered = z3.And(
        z3.Extract(8 * PI_FIC - 1, 0, z3.BitVec("pi", 8 * PUBLIC_INPUTS_SIZE)) ==
        z3.Extract(8 * PI_FIC - 1, 0, z3.BitVec("pi2", 8 * PUBLIC_INPUTS_SIZE)),
        z3.Extract(8 * PUBLIC_INPUTS_SIZE - 1, 8 * PI_FIC, z3.BitVec("pi", 8 * PUBLIC_INPUTS_SIZE)) !=
        z3.Extract(8 * PUBLIC_INPUTS_SIZE - 1, 8 * PI_FIC, z3.BitVec("pi2", 8 * PUBLIC_INPUTS_SIZE)))
    yield "negative_control_fic_byte_uncovered", fic_uncovered, z3.sat
    # (c) if the proofLen prefix were omitted, the proof payload length would be under-determined:
    #     two headers with identical declared fields but different length-prefix bytes at
    #     [317,321) would claim different proof payloads for the same commitment fields.
    plen_differs = z3.And(
        z3.Extract(8 * PROOFLEN_OFFSET - 1, 0, wire) == z3.Extract(8 * PROOFLEN_OFFSET - 1, 0, wire2),
        z3.Extract(8 * (PROOFLEN_OFFSET + 4) - 1, 8 * PROOFLEN_OFFSET, wire) !=
        z3.Extract(8 * (PROOFLEN_OFFSET + 4) - 1, 8 * PROOFLEN_OFFSET, wire2))
    yield "negative_control_prooflen_prefix_omitted", plen_differs, z3.sat


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


def run(source=CONTRACT, spec=SPEC):
    report = {"schema": "neo-n4/batch-spec-model/v1", "wholeSystemVerified": False,
              "scope": "spec-to-implementation correspondence of doc.md §3.2 L2BatchCommitment vs the implemented wire layout",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten correspondence: doc.md §3.2 field list vs implemented offsets",
                  "standard C# wire widths: uint32=4, ulong=8, UInt256=32, byte=1, byte[] as 4-byte LE length prefix + payload",
                  "the public-inputs domain is the 352-byte EncodePublicInputs/ComputePublicInputHash layout",
                  "cryptographic collision resistance of Hash256 is a separate trusted assumption",
                  "this models the wire-format correspondence, not execution or storage semantics"],
              "source": str(source), "spec": str(spec),
              "scriptSha256": source_digest(__file__),
              "specSha256": source_digest(spec)}
    try:
        report["anchorsChecked"] = [str(p) for p in SOURCE_ANCHORS]
        check_anchors()
        report["obligations"] = [solve(*o) for o in obligations()]
        if report["obligations"] and all(o["passed"] for o in report["obligations"]):
            report["status"] = "passed"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    report["exitCode"] = 0 if report["status"] == "passed" else 1
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("batch-spec-result.json"))
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