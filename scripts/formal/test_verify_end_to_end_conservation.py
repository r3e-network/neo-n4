"""Self-tests for end-to-end asset conservation verification model."""
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import z3
import verify_end_to_end_conservation as verification


class EndToEndConservationModelTests(unittest.TestCase):
    def test_all_obligations_and_negative_controls(self):
        """Verify all obligations pass and negative controls work."""
        report = verification.run()
        self.assertEqual("passed", report["status"], report.get("error"))
        self.assertEqual(0, report["exitCode"])
        self.assertFalse(report["wholeSystemVerified"])
        self.assertGreater(len(report["obligations"]), 10)
        
        # Check negative controls are present
        # Note: Some negative controls produce sat (counterexamples to safety),
        # others produce unsat (proving certain violations are impossible)
        negative = [o for o in report["obligations"] if o["name"].startswith("negative_control_")]
        self.assertGreaterEqual(len(negative), 3)
        
        # All negative controls should pass (whether sat or unsat)
        for neg in negative:
            self.assertTrue(neg["passed"], f"{neg['name']} did not pass")
            
        # Counterexample negative controls should have witnesses
        sat_negatives = [o for o in negative if o["actual"] == "sat"]
        for neg in sat_negatives:
            self.assertIn("witness", neg, f"{neg['name']} missing witness")
    
    def test_bridge_source_changed_requires_review(self):
        """Source digest mismatch should fail verification."""
        bridge_path = verification.BRIDGE_CONTRACT
        source = bridge_path.read_text(encoding="utf-8-sig")
        
        # Change source slightly
        anchor = "public static void OnNEP17Payment("
        self.assertIn(anchor, source)
        
        with tempfile.TemporaryDirectory() as tmpdir:
            modified = Path(tmpdir) / "SharedBridgeContract.cs"
            modified.write_text(source.replace(anchor, "public static void OnNEP17PaymentModified("), encoding="utf-8")
            report = verification.run(bridge_contract=modified)
        
        self.assertEqual(1, report["exitCode"])
        self.assertEqual([], report["obligations"])
        self.assertIn("re-review", report["error"])
    
    def test_rollup_source_changed_requires_review(self):
        """Rollup source digest mismatch should fail verification."""
        rollup_path = verification.ROLLUP_CONTRACT
        source = rollup_path.read_text(encoding="utf-8-sig")
        
        # Change source slightly
        anchor = "public static void RegisterChain("
        self.assertIn(anchor, source)
        
        with tempfile.TemporaryDirectory() as tmpdir:
            modified = Path(tmpdir) / "RollupHubContract.cs"
            modified.write_text(source.replace(anchor, "public static void RegisterChainModified("), encoding="utf-8")
            report = verification.run(rollup_contract=modified)
        
        self.assertEqual(1, report["exitCode"])
        self.assertEqual([], report["obligations"])
        self.assertIn("re-review", report["error"])
    
    def test_line_endings_do_not_change_digest(self):
        """CRLF vs LF should not affect digest."""
        bridge_text = verification.BRIDGE_CONTRACT.read_text(encoding="utf-8-sig")
        
        with tempfile.TemporaryDirectory() as tmpdir:
            crlf_version = Path(tmpdir) / "bridge_crlf.cs"
            crlf_version.write_bytes(bridge_text.replace("\n", "\r\n").encode())
            
            self.assertEqual(
                verification.REVIEWED_BRIDGE_SHA256,
                verification.source_digest(crlf_version)
            )
    
    def test_unknown_cannot_pass(self):
        """Z3 unknown result should fail all obligations."""
        with patch.object(z3.Solver, "check", return_value=z3.unknown):
            report = verification.run()
        
        self.assertEqual(1, report["exitCode"])
        self.assertTrue(all(not o["passed"] for o in report["obligations"]))
    
    def test_counterexample_in_proof_obligation_cannot_pass(self):
        """A sat result when expecting unsat should fail."""
        with patch.object(verification, "obligations", return_value=iter([
            ("broken_invariant", z3.Solver(), z3.BoolVal(True), z3.unsat)
        ])):
            report = verification.run()
        
        self.assertEqual(1, report["exitCode"])
        self.assertEqual("sat", report["obligations"][0]["actual"])
        self.assertEqual("unsat", report["obligations"][0]["expected"])
        self.assertFalse(report["obligations"][0]["passed"])


if __name__ == "__main__":
    unittest.main()