import unittest

from german_ui_text_audit import find_transliterations


class GermanUiTextAuditTests(unittest.TestCase):
    def test_detects_ascii_and_mixed_umlaut_transliterations(self):
        for value in ("schliessen", "einschliesslich", "Rückgaengig", "Rueckgaengig"):
            with self.subTest(value=value):
                self.assertTrue(find_transliterations(value))

    def test_accepts_correct_german_umlauts(self):
        self.assertEqual([], find_transliterations("Schließen, einschließlich, rückgängig"))


if __name__ == "__main__":
    unittest.main()
