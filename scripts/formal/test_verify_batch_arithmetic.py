"""Check the batch-arithmetic runner's obligations and negative controls."""
import unittest


class BatchArithmeticModelTests(unittest.TestCase):
    def test_all_obligations_and_negative_controls(self):
        import verify_batch_arithmetic as v
        results = []
        for name, solver, formula, expected in v.obligations():
            solver.push()
            solver.add(formula)
            result = solver.check()
            actual = str(result)
            passed = (actual == expected) if isinstance(expected, str) else (result == expected)
            solver.pop()
            results.append((name, expected, actual, passed))
        self.assertTrue(all(passed for *_, passed in results), [r for r in results if not r[3]])
        # 6 invariants + 3 negative controls.
        self.assertEqual(9, len(results))
        neg = [r for r in results if r[0].startswith("negative_control_")]
        self.assertEqual(3, len(neg))
        self.assertTrue(all(r[2] == "unsat" for r in neg))  # violated invariants are unsat

    def test_z3_importable(self):
        from z3 import Solver, Int
        self.assertTrue(callable(Solver))


if __name__ == "__main__":
    unittest.main()