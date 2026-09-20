"""Check the settlement-ABI correspondence runner's fail-closed behavior."""
import json
import unittest
from pathlib import Path
from unittest.mock import patch

import verify_settlement_abi as verification


class SettlementAbiModelTests(unittest.TestCase):
    def test_all_obligations(self):
        report = verification.run()
        self.assertEqual("passed", report["status"])
        self.assertEqual(0, report["exitCode"])
        self.assertFalse(report["wholeSystemVerified"])
        # 10 doc-declared methods + 3 revert/lock invariants + 4 registry surface = 17.
        self.assertEqual(17, len(report["obligations"]))
        self.assertTrue(all(o["actual"] is True for o in report["obligations"]))

    def test_missing_method_fails_closed(self):
        manifest = verification.manifest_from_artifacts()
        # Drop revertBatch from a copy of the manifest's method table.
        manifest["abi"]["methods"] = [
            m for m in manifest["abi"]["methods"] if m["name"] != "revertBatch"]
        results = list(verification.obligations(manifest))
        broken = [r for r in results if r[0] == "doc_method_implemented_revertBatch"]
        self.assertEqual(1, len(broken))
        self.assertFalse(broken[0][1])
        report = {"obligations": [verification.solve(*o) for o in results]}
        self.assertFalse(all(o["passed"] for o in report["obligations"]))

    def test_run_against_missing_manifest_fails_closed(self):
        report = verification.run(artifacts=Path("Z:/nonexistent/artifacts.cs"))
        self.assertEqual(1, report["exitCode"])
        self.assertIn("error", report)


if __name__ == "__main__":
    unittest.main()