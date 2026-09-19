"""Check the outbox-liveness runner's obligations and negative controls."""
import unittest


class OutboxLivenessModelTests(unittest.TestCase):
    def test_all_obligations_and_negative_controls(self):
        import verify_outbox_liveness as v
        results = []
        for name, kr, formula, expected in v.obligations():
            actual = v.formula_holds(kr, formula)
            results.append((name, expected, actual, actual == expected))
        # All pass.
        self.assertTrue(all(passed for *_, passed in results), [r for r in results if not r[3]])
        # 10 obligations total.
        self.assertEqual(10, len(results))
        # 3 negative controls all flip to False (a dropped transition kills liveness).
        neg = [r for r in results if r[0].startswith("negative_control_")]
        self.assertEqual(3, len(neg))
        self.assertTrue(all(r[2] is False for r in neg))

    def test_tool_importable(self):
        import pyModelChecking
        self.assertTrue(hasattr(pyModelChecking, "CTL"))
        import pyModelChecking.CTL.model_checking  # noqa: F401


if __name__ == "__main__":
    unittest.main()