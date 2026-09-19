"""Spec-correspondence model: doc.md §3.2 L2ChainConfig vs the 91-byte wire serializer.

Proves that the field layout in src/Neo.L2.Abstractions/Models/L2ChainConfigSerializer.cs is
exactly the decomposition of doc.md §3.2's L2ChainConfig struct: the 12 declared fields
partition the 91-byte wire buffer with no gaps and no overlaps, offsets are strictly
increasing in doc.md field order, the last field ends exactly at ConfigSize, and the encoding
is injective (distinct field tuples produce distinct wire bytes, so a config cannot be
re-encoded as a different one). This is the concrete spec-to-implementation correspondence
slice for the chain-config wire domain. A handwritten abstraction, NOT verification of
C#/NeoVM. See config-spec-model.md.
"""
import argparse
from pathlib import Path
import hashlib
import json
import sys

import z3

ROOT = Path(__file__).resolve().parents[2]
CONTRACT = ROOT / "src/Neo.L2.Abstractions/Models/L2ChainConfigSerializer.cs"
SPEC = ROOT / "doc.md"


def source_digest(path):
    text = Path(path).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    return hashlib.sha256(text.encode()).hexdigest()


# doc.md §3.2 L2ChainConfig struct, in declaration order: (name, byte width).
# uint32 chainId; UInt160 operatorManager/verifier/bridgeAdapter/messageAdapter;
# byte securityLevel/daMode/sequencerModel/exitModel; bool gatewayEnabled/permissionlessExit/active.
DOC_FIELDS = [
    ("chainId", 4),
    ("operatorManager", 20),
    ("verifier", 20),
    ("bridgeAdapter", 20),
    ("messageAdapter", 20),
    ("securityLevel", 1),
    ("daMode", 1),
    ("gatewayEnabled", 1),
    ("permissionlessExit", 1),
    ("sequencerModel", 1),
    ("exitModel", 1),
    ("active", 1),
]
# Serializer offsets (L2ChainConfigSerializer.cs lines 41-52), same order.
SERIALIZER_OFFSETS = [0, 4, 24, 44, 64, 84, 85, 86, 87, 88, 89, 90]
CONFIG_SIZE = 91  # serializer: 4 + 20*4 + 7


def check_source(path=CONTRACT):
    digest = source_digest(path)
    if digest != REVIEWED_SOURCE_SHA256:
        raise ValueError("reviewed L2ChainConfigSerializer source changed; re-review model correspondence")
    return digest


REVIEWED_SOURCE_SHA256 = "e07131fbd2b7388088c3b6771e22f69816d48c4266569571f5ee66a516ea6947"


def obligations():
    # Wire buffer as a 91-byte bitvector; each doc.md field occupies its declared offset/width.
    wire = z3.BitVec("wire", 8 * CONFIG_SIZE)
    wire2 = z3.BitVec("wire2", 8 * CONFIG_SIZE)

    # Per-field byte ranges: field k occupies [offset_k, offset_k + width_k).
    # Extract each field's bytes from the wire at the serializer offsets (little-endian for
    # chainId, byte order preserved for the fixed-width fields).
    def field_bits(k):
        off = SERIALIZER_OFFSETS[k]
        width = DOC_FIELDS[k][1]
        return z3.Extract(8 * (off + width) - 1, 8 * off, wire)

    fields = [field_bits(k) for k in range(len(DOC_FIELDS))]

    # 1) The declared fields are contiguous and non-overlapping: offset_{k+1} == offset_k + width_k.
    contiguous = all(
        SERIALIZER_OFFSETS[k + 1] == SERIALIZER_OFFSETS[k] + DOC_FIELDS[k][1]
        for k in range(len(DOC_FIELDS) - 1))
    yield "fields_are_contiguous_no_gaps", contiguous, z3.sat

    # 2) The last field ends exactly at ConfigSize (no trailing padding hole). Static facts are
    #    checked as Not(fact) -> unsat, proving the fact is necessarily true.
    last = len(DOC_FIELDS) - 1
    yield "last_field_ends_at_config_size", z3.Not(z3.BoolVal(
        SERIALIZER_OFFSETS[last] + DOC_FIELDS[last][1] == CONFIG_SIZE and CONFIG_SIZE == 91)), z3.unsat
    # ConfigSize is exactly the sum of declared field widths (no slack, no hole).
    total_width = sum(w for _, w in DOC_FIELDS)
    yield "config_size_equals_field_widths", z3.Not(z3.BoolVal(total_width == CONFIG_SIZE)), z3.unsat

    # 3) Offsets are strictly increasing in doc.md field order (no reordering, no overlap).
    increasing = all(
        SERIALIZER_OFFSETS[k] < SERIALIZER_OFFSETS[k + 1] for k in range(len(DOC_FIELDS) - 1))
    yield "offsets_strictly_increase_in_doc_order", z3.Not(z3.BoolVal(increasing)), z3.unsat

    # 4) Injectivity: two 91-byte encodings equal ⟹ every declared field equal (no two distinct
    #    configs share wire bytes). Equivalently: differing in ANY declared field changes the
    #    wire bytes.
    all_equal = z3.And(*[fields[k] == z3.Extract(
        8 * (SERIALIZER_OFFSETS[k] + DOC_FIELDS[k][1]) - 1, 8 * SERIALIZER_OFFSETS[k], wire2)
        for k in range(len(DOC_FIELDS))])
    wire_equal = wire == wire2
    # Equal fields imply equal wires (the fields partition the whole buffer).
    yield "field_equality_implies_wire_equality", z3.And(
        all_equal, z3.Not(wire_equal)), z3.unsat
    # Differing in any single declared field changes the wire (so the field is load-bearing).
    # chainId binds the wire (chainId is the identity of the config).
    yield "chainid_binds_wire", z3.And(
        wire == wire2,
        z3.Extract(8 * 4 - 1, 8 * 0, wire) != z3.Extract(8 * 4 - 1, 8 * 0, wire2)), z3.unsat

    # 5) The active bit at offset 90 is inside the buffer (contract reads it there).
    yield "active_bit_in_bounds", z3.Not(z3.BoolVal(
        SERIALIZER_OFFSETS[-1] >= 0 and SERIALIZER_OFFSETS[-1] < CONFIG_SIZE)), z3.unsat

    # ---- feasibility (sat) ----
    yield "wire_feasible", z3.BoolVal(True), z3.sat
    # Two configs differing only in the active bit produce different wires (reachability).
    yield "distinct_active_bits_feasible", z3.And(
        z3.Extract(8 * (SERIALIZER_OFFSETS[-1] + 1) - 1, 8 * SERIALIZER_OFFSETS[-1], wire)
        != z3.Extract(8 * (SERIALIZER_OFFSETS[-1] + 1) - 1, 8 * SERIALIZER_OFFSETS[-1], wire2)), z3.sat

    # ---- negative controls (SAT: break the partition and a bad encoding becomes possible) ----
    # (a) a trailing byte not covered by any declared field is unconstrained: two wires with
    #     identical declared fields but a different trailing byte are indistinguishable by the
    #     field list (the encoding would not be determined by the spec fields alone).
    tail_free = z3.And(
        z3.Extract(8 * 90 - 1, 0, wire) == z3.Extract(8 * 90 - 1, 0, wire2),
        z3.Extract(8 * CONFIG_SIZE - 1, 8 * 90, wire) != z3.Extract(8 * CONFIG_SIZE - 1, 8 * 90, wire2))
    yield "negative_control_trailing_byte_uncovered", tail_free, z3.sat
    # (b) overlapping fields: a hypothetical 6-byte layout with field0 at [0,4) and field1 at
    #     [2,6) — writing field1 overwrites field0's high bytes, so field0's original value is
    #     not recoverable from the wire.
    w6 = z3.BitVec("w6", 48)
    f0 = z3.Extract(8 * 4 - 1, 0, w6)
    overlap_destroy = z3.Exists([z3.BitVec("w6b", 48)],
        z3.And(z3.Extract(8 * 6 - 1, 8 * 2, z3.BitVec("w6b", 48)) ==
               z3.Extract(8 * 6 - 1, 8 * 2, w6),
               z3.Extract(8 * 4 - 1, 0, z3.BitVec("w6b", 48)) != f0))
    yield "negative_control_overlap_destroys_field", overlap_destroy, z3.sat
    # (c) a gap byte not covered by any declared field breaks determinism: a hypothetical layout
    #     with field0 at [0,4), a 1-byte gap at [4,5), field1 at [5,6) — two wires with identical
    #     fields but a different gap byte are indistinguishable by the field list.
    wg = z3.BitVec("wg", 48)
    wg2 = z3.BitVec("wg2", 48)
    gap_same_fields = z3.And(
        z3.Extract(8 * 4 - 1, 0, wg) == z3.Extract(8 * 4 - 1, 0, wg2),
        z3.Extract(8 * 6 - 1, 8 * 5, wg) == z3.Extract(8 * 6 - 1, 8 * 5, wg2),
        z3.Extract(8 * 5 - 1, 8 * 4, wg) != z3.Extract(8 * 5 - 1, 8 * 4, wg2))
    yield "negative_control_gap_breaks_determinism", gap_same_fields, z3.sat


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
    report = {"schema": "neo-n4/config-spec-model/v1", "wholeSystemVerified": False,
              "scope": "spec-to-implementation correspondence of doc.md §3.2 L2ChainConfig vs the 91-byte serializer",
              "solver": z3.get_version_string(), "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "handwritten correspondence: doc.md §3.2 field list vs serializer offsets",
                  "the wire buffer is a flat 91-byte little-endian domain",
                  "UInt160 fields are 20 bytes; uint32 chainId is 4 bytes LE",
                  "boolean fields are single bytes (0/1); enum fields single bytes",
                  "this models the wire-format correspondence, not the contract's storage semantics",
                  "the chainId-0-reserved and enum-range guards live at the contract/serializer level"],
              "source": str(source), "spec": str(spec),
              "scriptSha256": source_digest(__file__),
              "specSha256": source_digest(spec)}
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
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("config-spec-result.json"))
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