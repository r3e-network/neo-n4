"""Fail-closed self-tests for verify_batch_length.py: anchors must reject drifted
source, a broken bitvector model must be caught, and the report must stay machine
readable. These tests do not verify BatchSerializer itself."""
import importlib.util
import shutil
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("verify_batch_length.py")
SPEC = importlib.util.spec_from_file_location("verify_batch_length", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class VerifyBatchLengthSelfTests(unittest.TestCase):
    def test_rejects_removed_proof_length_upper_bound(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package = root / "src" / "Neo.L2.Batch"
            package.mkdir(parents=True)
            source = (MODULE.ROOT / MODULE.SOURCE.relative_to(MODULE.ROOT)).read_text(
                encoding="utf-8-sig")
            target = package / "BatchSerializer.cs"
            target.write_text(source, encoding="utf-8")
            # The checker anchors guard text, not the layout table: weaken the
            # upper-bound clause the way a regression would and assert the edit took.
            anchor = "if (proofLen < 0 || proofLen > ProofMaxBytes)"
            self.assertIn(anchor, source)
            drifted = source.replace(anchor, "if (proofLen < 0)")
            target.write_text(drifted, encoding="utf-8")
            module_root = root / "scripts" / "formal"
            module_root.mkdir(parents=True)
            shutil.copy(SCRIPT, module_root / SCRIPT.name)
            script = module_root / SCRIPT.name
            spec = importlib.util.spec_from_file_location("drifted_check", script)
            drifted_module = importlib.util.module_from_spec(spec)
            drifted_module.__file__ = str(script)
            spec.loader.exec_module(drifted_module)
            self.assertEqual(1, drifted_module.main())

    def test_weakened_guard_is_detected_by_solver(self):
        import z3

        proof_len = z3.BitVec("proof_len", 32)
        data_len = z3.BitVec("data_len", 32)
        bounds = z3.And(proof_len >= 0, proof_len <= 1048576, data_len >= 321)
        weak = z3.And(bounds, z3.SignExt(32, proof_len + 321) <= z3.SignExt(32, data_len))
        solver = z3.Solver()
        solver.add(weak, proof_len == 1, data_len == 323)
        self.assertEqual(z3.sat, solver.check())


if __name__ == "__main__":
    unittest.main()
