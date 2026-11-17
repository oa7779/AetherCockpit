import pathlib
import unittest

from encoding_audit import TEXT_SUFFIXES, UTF8_BOM, audit_content, is_text_path


class EncodingAuditTests(unittest.TestCase):
    def test_accepts_utf8_and_crlf(self):
        self.assertEqual([], audit_content("src/example.cs", "Prüfen\r\n".encode("utf-8")))

    def test_rejects_invalid_utf8_and_replacement_character(self):
        self.assertTrue(audit_content("src/example.cs", b"\xff"))
        self.assertIn(
            "contains U+FFFD replacement character",
            audit_content("src/example.cs", (chr(0xFFFD) + "\r\n").encode("utf-8")),
        )

    def test_unicode_power_shell_requires_bom(self):
        self.assertEqual(
            ["non-ASCII script text requires a UTF-8 BOM"],
            audit_content("build/example.ps1", "Write-Output 'Größe'\r\n".encode("utf-8")),
        )
        self.assertEqual(
            [],
            audit_content("build/example.ps1", UTF8_BOM + "Write-Output 'Größe'\r\n".encode("utf-8")),
        )

    def test_rejects_mixed_and_lf_only_endings(self):
        self.assertIn(
            "mixed or non-CRLF line endings",
            audit_content("docs/example.md", b"one\r\ntwo\n"),
        )
        self.assertIn(
            "mixed or non-CRLF line endings",
            audit_content("docs/example.md", b"one\ntwo\n"),
        )

    def test_known_historical_line_endings_are_preserved(self):
        self.assertEqual(
            [],
            audit_content("docs/MILESTONE-0-CLOSURE-REPORT.md", b"one\r\ntwo\n"),
        )
        self.assertEqual(
            [],
            audit_content("tools/PresentMon-LICENSE.txt", b"one\r\ntwo\n"),
        )

    def test_requested_text_file_extensions_are_scanned(self):
        for filename in ("solution.sln", "settings.jsonc", "build.bat", "setup.cmd", "NuGet.Config"):
            with self.subTest(filename=filename):
                self.assertTrue(is_text_path(pathlib.Path(filename)))
        self.assertTrue({".sln", ".jsonc", ".bat", ".cmd"} <= TEXT_SUFFIXES)


if __name__ == "__main__":
    unittest.main()
