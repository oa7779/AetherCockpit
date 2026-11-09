import unittest

from desktop_localization_audit import release_blockers


class ReleaseGateTests(unittest.TestCase):
    def report(self):
        return {
            "missing_explicit_resources": [],
            "source_safety_checks": {"restart": True},
            "confirmed_untranslated_sites_detected": 0,
            "counts_by_classification": {},
            "english_complete": True,
        }

    def test_complete_review_passes(self):
        self.assertEqual([], release_blockers(self.report()))

    def test_each_incomplete_condition_blocks(self):
        cases = [
            ("missing_explicit_resources", ["missing"]),
            ("source_safety_checks", {"restart": False}),
            ("confirmed_untranslated_sites_detected", 1),
            ("counts_by_classification", {"unreviewed-display": 1}),
            ("counts_by_classification", {"unreviewed-source": 1}),
            ("english_complete", False),
        ]
        for key, value in cases:
            with self.subTest(key=key):
                report = self.report()
                report[key] = value
                self.assertTrue(release_blockers(report))


if __name__ == "__main__":
    unittest.main()
