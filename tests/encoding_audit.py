"""Verify tracked text files use valid UTF-8 and safe script BOMs."""
import os
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
UTF8_BOM = b"\xef\xbb\xbf"
TEXT_SUFFIXES = {
    ".cs", ".xaml", ".resx", ".csproj", ".props", ".targets", ".json", ".jsonc",
    ".xml", ".manifest", ".ps1", ".psm1", ".psd1", ".iss", ".py", ".md",
    ".txt", ".yml", ".yaml", ".sln", ".slnx", ".rc", ".config", ".bat", ".cmd",
}
TEXT_FILENAMES = {".editorconfig", ".gitattributes"}
SCRIPT_SUFFIXES = {".ps1", ".psm1", ".psd1", ".iss"}
MIXED_EOL_ALLOWLIST = {
    "docs/MILESTONE-0-CLOSURE-REPORT.md",
    "docs/RC-SOURCE-READING-MANIFEST.md",
    "tools/PresentMon-LICENSE.txt",
}


def audit_content(relative_path, content):
    failures = []
    path = pathlib.PurePosixPath(relative_path)
    has_bom = content.startswith(UTF8_BOM)
    script = path.suffix.lower() in SCRIPT_SUFFIXES
    if has_bom and not script:
        failures.append("unexpected UTF-8 BOM")
    if script and any(byte >= 0x80 for byte in content) and not has_bom:
        failures.append("non-ASCII script text requires a UTF-8 BOM")

    payload = content[len(UTF8_BOM):] if has_bom else content
    try:
        text = payload.decode("utf-8", errors="strict")
    except UnicodeDecodeError as error:
        return [f"invalid UTF-8: {error}"]
    if "\ufffd" in text:
        failures.append("contains U+FFFD replacement character")

    crlf = text.count("\r\n")
    lf = text.count("\n") - crlf
    cr = text.count("\r") - crlf
    if (cr or lf or (crlf and lf)) and relative_path not in MIXED_EOL_ALLOWLIST:
        failures.append("mixed or non-CRLF line endings")
    return failures


def is_text_path(path):
    return path.name.lower() in TEXT_FILENAMES or path.suffix.lower() in TEXT_SUFFIXES


def text_paths():
    tracked_result = subprocess.run(
        ["git", "ls-files", "--cached", "-z"],
        cwd=ROOT,
        check=True,
        stdout=subprocess.PIPE,
    )
    tracked = {os.fsdecode(entry) for entry in tracked_result.stdout.split(b"\0") if entry}
    other_result = subprocess.run(
        ["git", "ls-files", "--others", "--exclude-standard", "-z"],
        cwd=ROOT,
        check=True,
        stdout=subprocess.PIPE,
    )
    paths = []
    for entry in other_result.stdout.split(b"\0"):
        if not entry:
            continue
        relative = os.fsdecode(entry)
        path = ROOT / relative
        if relative in tracked:
            continue
        if is_text_path(path):
            paths.append(path)
    for relative in tracked:
        path = ROOT / relative
        if is_text_path(path):
            paths.append(path)
    return paths


def main():
    failures = []
    scanned = 0
    for path in text_paths():
        try:
            content = path.read_bytes()
        except OSError as error:
            failures.append(f"{path.relative_to(ROOT)}: cannot read: {error}")
            continue

        for error in audit_content(path.relative_to(ROOT).as_posix(), content):
            failures.append(f"{path.relative_to(ROOT)}: {error}")
        scanned += 1

    if failures:
        print("\n".join(failures))
        return 1
    print(f"Encoding audit passed for {scanned} text files.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
