"""Encoding regressions are invisible in C# and fatal in XAML and resx.

This project keeps the UTF-8 BOM on its source files. An editor or patch tool that
rewrites a file without it compiles fine but breaks at runtime: WPF XAML parsing, the
resource designer, and the flag converters all depend on the byte order mark. These
checks compare every tracked source file against the upstream baseline so a stripped
BOM is caught here rather than as an unexplained broken column in the UI.
"""

import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASELINE = "27118ce9~1"  # before the NiN quality work began
SUFFIXES = (".cs", ".xaml", ".axaml", ".resx")
BOM = b"\xef\xbb\xbf"


def tracked_sources():
    out = subprocess.run(
        ["git", "ls-files"], cwd=ROOT, capture_output=True, text=True).stdout
    rels = [r for r in out.split() if r.endswith(SUFFIXES)]
    return [ROOT / r for r in rels]


def had_bom_at(rel):
    r = subprocess.run(["git", "show", f"{BASELINE}:{rel}"], cwd=ROOT, capture_output=True)
    return r.returncode == 0 and r.stdout[:3] == BOM


class NiNEncodingTests(unittest.TestCase):
    def test_files_are_readable(self):
        self.assertGreater(len(tracked_sources()), 50,
                           "git ls-files returned nothing; the check would pass vacuously")

    def test_no_file_lost_its_bom(self):
        stripped = []
        for path in tracked_sources():
            rel = path.relative_to(ROOT).as_posix()
            if path.read_bytes()[:3] == BOM:
                continue
            if had_bom_at(rel):
                stripped.append(rel)
        self.assertEqual(
            stripped, [],
            "these files had a UTF-8 BOM upstream and lost it. WPF XAML, the resx "
            f"designer and the flag converters all need it: {stripped}",
        )

    def test_no_file_gained_a_bom_it_did_not_have(self):
        """The mirror image: adding a BOM is just as much an unasked-for change.

        A patch tool or a full-file rewrite that normalises the encoding is the
        usual cause. The BOM has to be part of a deliberate decision, not a side
        effect of editing.
        """
        gained = [
            path.relative_to(ROOT).as_posix()
            for path in tracked_sources()
            if path.read_bytes()[:3] == BOM
            and not had_bom_at(path.relative_to(ROOT).as_posix())
        ]
        self.assertEqual(
            gained, [],
            "these files gained a UTF-8 BOM they never had; revert the encoding "
            f"change: {gained}",
        )

    def test_resx_designer_keeps_its_bom(self):
        designer = ROOT / "v2rayN/ServiceLib/Resx/ResUI.Designer.cs"
        self.assertTrue(designer.exists())
        self.assertEqual(designer.read_bytes()[:3], BOM,
                         "ResUI.Designer.cs is a generated file; regenerating or "
                         "rewriting it without the BOM breaks every resx lookup")
