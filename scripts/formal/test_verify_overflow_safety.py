"""Check the overflow-safety runner's obligations and negative controls."""
import unittest


class OverflowSafetyModelTests(unittest.TestCase):
    def test_all_obligations_and_negative_controls(self):
        import verify_overflow_safety as v
        results = []
        for name, solver, formula, expected in v.obligations():
            solver.push()
            solver.add(formula)
            result = solver.check()
            actual = str(result)
            passed = (actual == str(expected))
            solver.pop()
            results.append((name, expected, actual, passed))
        self.assertTrue(all(passed for *_, passed in results), [r for r in results if not r[3]])
        # 6 safety invariants + 2 negative controls.
        self.assertEqual(8, len(results))
        neg = [r for r in results if r[0].startswith("negative_control_")]
        self.assertEqual(2, len(neg))

    def test_z3_importable(self):
        from z3 import Solver, BitVec
        self.assertTrue(callable(Solver))


if __name__ == "__main__":
    unittest.main()