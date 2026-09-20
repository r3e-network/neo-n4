"""Check the bridge-ABI correspondence runner's fail-closed behavior."""
import json
import unittest
from pathlib import Path

import verify_bridge_abi as verification


class BridgeAbiModelTests(unittest.TestCase):
    def test_all_obligations(self):
        report = verification.run()
        self.assertEqual("passed", report["status"])
        self.assertEqual(0, report["exitCode"])
        self.assertFalse(report["wholeSystemVerified"])
        # 8 doc-declared methods + L2 anchors + 2 rename/merge pins + 4 finalize variants
        # + deposit arity = 16.
        self.assertEqual(16, len(report["obligations"]))
        self.assertTrue(all(o["actual"] is True for o in report["obligations"]))

    def test_missing_method_fails_closed(self):
        manifest = verification.manifest_from_artifacts()
        manifest["abi"]["methods"] = [
            m for m in manifest["abi"]["methods"] if m["name"] != "sendMessage"]
        results = list(verification.obligations(manifest))
        broken = [r for r in results if r[0] == "bridge_method_implemented_sendMessage"]
        self.assertEqual(1, len(broken))
        self.assertFalse(broken[0][1])

    def test_rename_drift_is_caught(self):
        # Re-introducing the stale doc-era name (isMessageConsumed) is flagged as drift.
        manifest = verification.manifest_from_artifacts()
        manifest["abi"]["methods"].append({"name": "isMessageConsumed", "parameters": [], "safe": True})
        results = list(verification.obligations(manifest))
        renamed = [r for r in results if r[0] == "renamed_message_consumed_pinned"]
        self.assertEqual(1, len(renamed))
        self.assertFalse(renamed[0][1])

    def test_run_against_missing_manifest_fails_closed(self):
        report = verification.run(artifacts=Path("Z:/nonexistent/artifacts.cs"))
        self.assertEqual(1, report["exitCode"])
        self.assertIn("error", report)


if __name__ == "__main__":
    unittest.main()