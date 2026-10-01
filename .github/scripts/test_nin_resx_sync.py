"""Every key used in XAML must exist in the Designer class, and vice versa.

ResUI.Designer.cs is committed rather than generated, so adding a key to
ResUI.resx alone compiles the resx but leaves the XAML binding unable to resolve
it: `error MC3011: Cannot find the static member`. This walks every key in the
resx and requires a matching Designer property, which catches the half-applied
case before CI does.

The per-locale ResUI.<culture>.resx files are deliberately incomplete (upstream
ships them that way) and are NOT checked for completeness here.
"""
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RESX = REPO / "v2rayN/ServiceLib/Resx/ResUI.resx"
DESIGNER = REPO / "v2rayN/ServiceLib/Resx/ResUI.Designer.cs"


def read(path):
    return path.read_text(encoding="utf-8-sig")


# The resx schema comment at the top contains example <data name="Name1"> blocks
# that are documentation, not resources, so parse the live XML instead.
PLACEHOLDER_KEYS = {"Name1", "Color1", "Bitmap1", "Icon1"}


def resx_keys():
    text = read(RESX)
    keys = set(re.findall(r'<data\s+name="([^"]+)"', text))
    return keys - PLACEHOLDER_KEYS


def designer_props():
    return set(re.findall(r'public\s+static\s+string\s+(\w+)\s*\{', read(DESIGNER)))


def xaml_keys():
    """Static resx members referenced from any XAML/Axaml file."""
    found = set()
    for path in REPO.glob("v2rayN/**/*.xaml"):
        found |= set(re.findall(r'ResUI\.(\w+)', read(path)))
    for path in REPO.glob("v2rayN/**/*.axaml"):
        found |= set(re.findall(r'ResUI\.(\w+)', read(path)))
    return found


class ResxDesignerSyncTests(unittest.TestCase):
    def test_designer_matches_resx_exactly(self):
        missing = resx_keys() - designer_props()
        self.assertEqual(missing, set(), f"in ResUI.resx but not in Designer: {sorted(missing)}")

    def test_designer_has_no_orphan_properties(self):
        orphan = designer_props() - resx_keys()
        self.assertEqual(orphan, set(), f"in Designer but not in ResUI.resx: {sorted(orphan)}")

    def test_every_xaml_reference_resolves(self):
        unresolved = xaml_keys() - designer_props()
        self.assertEqual(unresolved, set(), f"referenced in XAML but missing: {sorted(unresolved)}")

if __name__ == "__main__":
    unittest.main()