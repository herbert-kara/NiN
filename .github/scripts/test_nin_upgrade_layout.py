"""The in-app updater must be able to replace files in NiN's own package layout.

NiN ships its package with the files at the zip ROOT (NiN.exe, AmazTool.exe,
bin/... -- 47 entries, no wrapper folder). AmazTool, the helper the app runs to
unpack a downloaded update, was written for upstream's zip, which wraps
everything in one top-level folder: it skipped any entry whose path had no
separator and stripped the first segment of the rest. On a NiN package that
meant NiN.exe was never written at all and bin files landed in the wrong place
-- the update finished, the app restarted, and the version was still the old
one. That is exactly "I update and the version does not change".
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPGRADE = ROOT / "v2rayN/AmazTool/UpgradeApp.cs"


class NiNUpgradeLayoutTests(unittest.TestCase):
    def test_root_entries_are_no_longer_skipped(self):
        src = UPGRADE.read_text(encoding="utf-8-sig")
        m = re.search(r"var fullName = .*?;", src, re.S)
        self.assertIsNotNone(m, "the entry path resolution is missing")
        # The old code skipped any entry without a separator, which is every
        # executable in a NiN package.
        self.assertNotIn(
            "lst.Length == 1",
            src,
            "entries without a separator are the zip's executables: skipping "
            "them makes the update replace nothing")

    def test_the_first_segment_is_only_stripped_for_a_real_wrapper(self):
        src = UPGRADE.read_text(encoding="utf-8-sig")
        m = re.search(r"private static string FindWrapperFolder\(.*?\n    \}", src, re.S)
        self.assertIsNotNone(
            m,
            "the wrapper detection is missing: without it the old code strips a "
            "segment from a flat zip and moves bin files to the wrong place")
        block = m.group(0)
        self.assertIn(
            "IndexOf('/')",
            block,
            "the detector must look at where the entry path's first separator "
            "is: a file with none sits at the zip root")
        self.assertIn(
            "return string.Empty",
            block,
            "a zip with files at the root (or several top-level folders) must be "
            "treated as flat, not stripped of a first segment")

    def test_root_entry_resolution_uses_the_entry_path(self):
        src = UPGRADE.read_text(encoding="utf-8-sig")
        self.assertRegex(
            src,
            r"var fullName = wrapper\.Length > 0\s*\?\s*entry\.FullName\[",
            "only a wrapped zip may lose its first segment")
        self.assertIn(
            "FindWrapperFolder(archive.Entries)",
            src,
            "the wrapper decision must come from FindWrapperFolder, not from a "
            "hardcoded assumption about the layout")

    def test_self_replacement_is_kept(self):
        # AmazTool replaces itself too; losing that would leave a stale updater
        # beside a fresh app.
        src = UPGRADE.read_text(encoding="utf-8-sig")
        self.assertIn("File.Move(Utils.GetExePath(), thisAppOldFile)", src,
                      "the running exe must still be moved aside before overwrite")
        self.assertIn("TryExtractToFile(entry, entryOutputPath)", src,
                      "extraction with retry must stay")


if __name__ == "__main__":
    unittest.main()
