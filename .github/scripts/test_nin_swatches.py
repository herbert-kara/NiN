"""NiN accent colours must land in the theme picker with the exact hex asked for."""
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SWATCHES_CS = REPO / "v2rayN/v2rayN/Common/NiNSwatches.cs"
VM_CS = REPO / "v2rayN/v2rayN/ViewModels/ThemeSettingViewModel.cs"

# The palette requested for NiN, in the order given.
# Foreground: white on the five dark colours, black on the two light ones
# (white-on-olive is only 2.6:1 and white-on-mauve 2.9:1, both unreadable).
REQUESTED = [
    ("NiN Olive", "97A87A", "000000"),
    ("NiN Teal Blue", "2D7495", "FFFFFF"),
    ("NiN Steel Blue", "455B8A", "FFFFFF"),
    ("NiN Deep Green", "2A6B5C", "FFFFFF"),
    ("NiN Wine", "972828", "FFFFFF"),
    ("NiN Mauve", "A290B7", "000000"),
    ("NiN Crimson", "CB2957", "FFFFFF"),
]


def _srgb_lin(c):
    c = c / 255
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _luminance(hexs):
    r, g, b = (int(hexs[i:i + 2], 16) for i in (0, 2, 4))
    return 0.2126 * _srgb_lin(r) + 0.7152 * _srgb_lin(g) + 0.0722 * _srgb_lin(b)


def _contrast(a, b):
    la, lb = _luminance(a), _luminance(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


class NiNSwatchPaletteTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.src = SWATCHES_CS.read_text(encoding="utf-8-sig")
        cls.vm = VM_CS.read_text(encoding="utf-8-sig")

    def test_file_exists(self):
        self.assertTrue(SWATCHES_CS.exists(), "NiNSwatches.cs is missing")

    def test_every_requested_colour_is_declared(self):
        for name, hexs, _fg in REQUESTED:
            with self.subTest(colour=name):
                self.assertRegex(self.src, rf'new\("{re.escape(name)}",\s*"{hexs}",\s*"[0-9A-F]{{6}}"\)')

    def test_no_extra_or_missing_colours(self):
        found = re.findall(r'new\("([^"]+)",\s*"([0-9A-Fa-f]{6})",\s*"([0-9A-Fa-f]{6})"\)', self.src)
        self.assertEqual(found, REQUESTED, "palette drifted from what was requested")

    def test_hex_values_are_valid_six_digit_rgb(self):
        for name, hexs, fg in REQUESTED:
            for label, value in ((name, hexs), (name + " fg", fg)):
                with self.subTest(entry=label):
                    self.assertRegex(value, r"^[0-9A-F]{6}$")

    def test_declared_foreground_is_the_readable_one(self):
        # The ramp applies one foreground to every shade, so it must be the better
        # of black/white on the pure colour. Getting this backwards makes the accent
        # unreadable, which is exactly what a hardcoded white caused.
        for name, hexs, fg in REQUESTED:
            with self.subTest(colour=name):
                chosen = _contrast(hexs, fg)
                other = _contrast(hexs, "FFFFFF" if fg == "000000" else "000000")
                self.assertGreaterEqual(chosen, 4.5, f"{name}: {fg} text is too low contrast")
                self.assertGreaterEqual(chosen, other, f"{name}: {fg} is not the better choice")

    def test_white_is_not_hardcoded_in_the_ramp(self):
        self.assertNotIn("new Hue(names[i], color, Colors.White)", self.src,
                         "foreground must come from the palette entry, not a constant")
        self.assertIn("new Hue(names[i], color, foreground)", self.src)

    def test_swatch_ramp_is_built_for_material_design(self):
        # MaterialDesign reads ExemplarHue (index 5 = the 500 shade) and derives
        # hover/pressed from the rest, so a full ramp must be produced.
        self.assertIn("new Swatch(item.Name, Hues(hue, foreground), Hues(hue, foreground))", self.src)
        self.assertRegex(self.src, r'"50".*"100".*"500"')

    def test_exemplar_hue_is_exactly_the_requested_colour(self):
        # mixWhite[5] must be 0.0 so the 500 entry is the pure colour, not a mix.
        m = re.search(r"var mixWhite = new\[\] \{([^}]*)\}", self.src)
        self.assertIsNotNone(m, "mixWhite ramp not found")
        values = [float(x) for x in m.group(1).split(",")]
        self.assertEqual(len(values), 10, "Material shade ramp must have 10 steps")
        self.assertEqual(values[5], 0.0, "shade 500 must be the pure requested colour")

    def test_picker_and_restore_both_use_the_nin_swatches(self):
        # Listing them in the dropdown but not when restoring a saved colour would
        # silently revert the accent on the next launch.
        self.assertIn("Swatches.AddRange(NiNSwatches.Build());", self.vm)
        self.assertRegex(
            self.vm,
            r"NiNSwatches\.Build\(\)\s*\.Concat\(new SwatchesProvider\(\)\.Swatches\)",
        )
        self.assertEqual(self.vm.count("NiNSwatches.Build()"), 2)

    def test_stock_material_palette_is_kept(self):
        self.assertIn("Swatches.AddRange(new SwatchesProvider().Swatches);", self.vm)


if __name__ == "__main__":
    unittest.main(verbosity=2)
