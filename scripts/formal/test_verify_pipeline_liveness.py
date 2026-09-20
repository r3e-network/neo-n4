"""Check the pipeline-liveness runner's obligations and negative controls."""
import unittest


class PipelineLivenessModelTests(unittest.TestCase):
    def test_all_obligations_and_negative_controls(self):
        import verify_pipeline_liveness as v
        results = []
        for name, kr, formula, expected in v.obligations():
            actual = v.holds(kr, formula)
            results.append((name, expected, actual, actual == expected))
        self.assertTrue(all(passed for *_, passed in results), [r for r in results if not r[3]])
        # 5 cross-component liveness + 2 negative controls.
        self.assertEqual(7, len(results))
        neg = [r for r in results if r[0].startswith("negative_control_")]
        self.assertEqual(2, len(neg))
        self.assertTrue(all(r[2] is True for r in neg))  # bad state reachable when guard dropped

    def test_tool_importable(self):
        import pyModelChecking
        self.assertTrue(hasattr(pyModelChecking, "CTL"))


if __name__ == "__main__":
    unittest.main()