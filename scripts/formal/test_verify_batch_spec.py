"""Check the batch-spec proof runner's fail-closed behavior and negative controls."""
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import z3
import verify_batch_spec as verification


class BatchSpecModelTests(unittest.TestCase):
    def test_all_obligations_and_negative_controls(self):
        report = verification.run()
        self.assertEqual("passed", report["status"])
        self.assertEqual(0, report["exitCode"])
        self.assertFalse(report["wholeSystemVerified"])
        self.assertEqual(13, len(report["obligations"]))
        negative = [item for item in report["obligations"]
                    if item["name"].startswith("negative_control_")]
        self.assertEqual(3, len(negative))
        self.assertTrue(all(item["actual"] == "sat" and item.get("witness") for item in negative))

    def test_anchor_drift_fails_closed(self):
        # Remove a layout anchor from a temp copy of the serializer and expect re-review.
        source = verification.CONTRACT.read_text(encoding="utf-8-sig")
        anchor = "Total = 4 + 8 + 8 + 8 + 9×32 + 1 + 4 = 321 bytes."
        self.assertIn(anchor, source)
        with tempfile.TemporaryDirectory() as directory:
            changed = Path(directory) / "BatchSerializer.cs"
            changed.write_text(source.replace(anchor, ""), encoding="utf-8")
            # Point the anchor set at the temp copy.
            original = dict(verification.SOURCE_ANCHORS)
            try:
                verification.SOURCE_ANCHORS.clear()
                verification.SOURCE_ANCHORS[changed] = [anchor]
                report = verification.run(changed)
            finally:
                verification.SOURCE_ANCHORS.clear()
                verification.SOURCE_ANCHORS.update(original)
        self.assertEqual(1, report["exitCode"])
        self.assertEqual([], report["obligations"])
        self.assertIn("anchor changed", report["error"])

    def test_unknown_cannot_pass(self):
        with patch.object(z3.Solver, "check", return_value=z3.unknown):
            report = verification.run()
        self.assertEqual(1, report["exitCode"])
        self.assertTrue(all(not item["passed"] for item in report["obligations"]))

    def test_counterexample_in_proof_obligation_cannot_pass(self):
        with patch.object(verification, "obligations", return_value=iter([
                ("broken_invariant", z3.BoolVal(True), z3.unsat)])):
            report = verification.run()
        self.assertEqual(1, report["exitCode"])
        self.assertEqual("sat", report["obligations"][0]["actual"])


if __name__ == "__main__":
    unittest.main()