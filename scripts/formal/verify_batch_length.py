"""Check the signed-32-bit arithmetic behind BatchSerializer.Decode length validation.

Scope: a symbolic model of the extracted guards, not verification of the C# compiler,
all decoder statements, payload contents, cryptography, or whole-system correctness.
Requires z3-solver==4.15.3.0. See doc.md sections 7.2 and 8.
"""
from pathlib import Path
import hashlib
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "src/Neo.L2.Batch/BatchSerializer.cs"
REPORT = Path(__file__).with_name("batch-length-result.txt")


def main():
    lines = ["BatchSerializer.Decode proof-length arithmetic verification",
             "scope=signed 32-bit length guards; whole_system_verified=false"]
    exit_code = 1
    try:
        import z3

        source_bytes = SOURCE.read_bytes()
        source = source_bytes.decode("utf-8-sig")
        lines.extend([
            f"source={SOURCE.relative_to(ROOT).as_posix()}",
            f"source_sha256={hashlib.sha256(source_bytes).hexdigest()}",
            f"script_sha256={hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}",
            f"solver={z3.get_version_string()}",
            "assumptions=buffer length is a nonnegative C# int; proofLen is a signed int32; "
            "pos is 321 after the fixed header; C# comparisons are signed",
            "source_link=guard text and cap checked; pos/layout and runtime semantics are trusted, "
            "not derived by a C# verifier",
        ])
        # Exact source anchors fail closed on guard drift. They are not a C# parser.
        clean = re.sub(r"//[^\n]*", "", source)
        clean = re.sub(r"\s+", " ", clean)
        cap = "private const int ProofMaxBytes = 1 * 1024 * 1024;"
        if cap not in clean:
            raise ValueError("proof cap source anchor changed")
        start = clean.index("public static L2BatchCommitment Decode(ReadOnlySpan<byte> data)")
        end = clean.index("public static byte[] EncodePublicInputs", start)
        decoder = clean[start:end]
        for anchor in (
            "if (data.Length < CommitmentFixedSize)",
            "var proofLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4)); pos += 4;",
            "if (proofLen < 0 || proofLen > ProofMaxBytes)",
            "if (pos + proofLen != data.Length)",
            "var proof = data.Slice(pos, proofLen).ToArray();",
        ):
            if anchor not in decoder:
                raise ValueError(f"decoder source anchor changed: {anchor}")

        proof_len = z3.BitVec("proof_len", 32)
        data_len = z3.BitVec("data_len", 32)
        total = proof_len + 321
        bounds = z3.And(proof_len >= 0, proof_len <= 1048576, data_len >= 321)
        accepted = z3.And(bounds, total == data_len)
        mathematical_total = z3.SignExt(32, proof_len) + z3.BitVecVal(321, 64)

        def check(name, formula, expected):
            solver = z3.Solver()
            solver.set(timeout=10000)
            solver.add(formula)
            result = solver.check()
            lines.append(f"{name}: {result} (expected {expected})")
            if result == z3.sat:
                lines.append(f"  witness={solver.model()}")
            if result != expected:
                raise RuntimeError(f"{name} failed: {result}; {solver.reason_unknown()}")

        check("accepted_domain_nonempty", accepted, z3.sat)
        check("zero_length_accepted", z3.And(accepted, proof_len == 0), z3.sat)
        check("maximum_length_accepted", z3.And(accepted, proof_len == 1048576), z3.sat)
        check("signed_addition_overflow_counterexample",
              z3.And(bounds, z3.SignExt(32, total) != mathematical_total), z3.unsat)
        check("accepted_length_mismatch_counterexample",
              z3.And(accepted, z3.SignExt(32, data_len) != mathematical_total), z3.unsat)
        check("accepted_invalid_payload_length_counterexample",
              z3.And(accepted, z3.Or(proof_len < 0, proof_len > 1048576)), z3.unsat)
        # A weakened equality guard must admit trailing bytes: sanity-check the checker.
        weak = z3.And(bounds, total <= data_len)
        check("negative_control_weakened_guard_accepts_trailing_byte",
              z3.And(weak, proof_len == 1, data_len == 323), z3.sat)
        lines.append("result=PROVEN for stated arithmetic model and assumptions only")
        exit_code = 0
    except Exception as error:
        lines.append(f"result=FAILED: {type(error).__name__}: {error}")
    lines.append(f"exit_code={exit_code}")
    output = "\n".join(lines) + "\n"
    REPORT.write_text(output, encoding="utf-8")
    print(output, end="")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
