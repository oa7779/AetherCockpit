import unittest

from desktop_localization_audit import release_blockers, review_site


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

    def test_translation_lookup_key_does_not_hide_unreviewed_content(self):
        item = {"path": r"src\SystemCockpit\Services\TaskCatalogEnglish.cs", "line": 1,
                "classification": "authored-model-or-script-candidate-requires-review",
                "text": '["Schutzstatus pruefen"] = new("Check protection", "READ ONLY",'}
        self.assertEqual("preserved-invariant-identifier", review_site(item)[0])
        item["classification"] = "untranslated-direct-ui-confirmed"
        item["text"] = 'Status.Text = "Einstellungen pruefen";'
        self.assertEqual("unreviewed-source", review_site(item)[0])


if __name__ == "__main__":
    unittest.main()
