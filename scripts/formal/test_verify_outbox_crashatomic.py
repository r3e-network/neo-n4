"""Check the outbox-crashatomic proof runner's fail-closed behavior."""
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import z3
import verify_outbox_crashatomic as verification


class OutboxCrashAtomicModelTests(unittest.TestCase):
    def test_all_obligations(self):
        report = verification.run()
        self.assertEqual("passed", report["status"])
        self.assertEqual(0, report["exitCode"])
        self.assertFalse(report["wholeSystemVerified"])
        self.assertEqual(9, len(report["obligations"]))

    def test_changed_source_requires_review(self):
        source = verification.CONTRACT.read_text(encoding="utf-8-sig")
        anchor = '_store.Put(PublicationKey, EncodeCheckpoint(checkpoint));'
        self.assertIn(anchor, source)
        with tempfile.TemporaryDirectory() as directory:
            changed = Path(directory) / "GatewayOutbox.cs"
            changed.write_text(source.replace(anchor, ""), encoding="utf-8")
            report = verification.run(changed)
        self.assertEqual(1, report["exitCode"])
        self.assertEqual([], report["obligations"])
        self.assertIn("re-review", report["error"])

    def test_line_endings_do_not_change_review_digest(self):
        text = verification.CONTRACT.read_text(encoding="utf-8-sig")
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "source.cs"
            target.write_bytes(text.replace("\n", "\r\n").encode())
            self.assertEqual(verification.REVIEWED_SOURCE_SHA256, verification.check_source(target))

    def test_unknown_cannot_pass(self):
        with patch.object(z3.Solver, "check", return_value=z3.unknown):
            report = verification.run()
        self.assertEqual(1, report["exitCode"])
        self.assertTrue(all(not item["passed"] for item in report["obligations"]))


if __name__ == "__main__":
    unittest.main()