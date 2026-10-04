"""Score/Jitter columns must never show a misleading number.

Users run several kinds of test: a real in-tunnel ping (which can measure
spread and loss) and a plain delay/tcping/udp probe (which cannot). The grid
has to distinguish "measured 0" from "never measured", otherwise every row
reads score 0 / jitter -1 and looks broken.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEEDTEST = ROOT / "v2rayN/ServiceLib/Services/SpeedtestService.cs"
MODEL = ROOT / "v2rayN/ServiceLib/Models/Dto/ProfileItemModel.cs"
WPF = ROOT / "v2rayN/v2rayN/Views/ProfilesView.xaml"
AVLN = ROOT / "v2rayN/v2rayN.Desktop/Views/ProfilesView.axaml"


def calls(text, needle):
    return len(re.findall(needle, text))


class NiNQualityDisplayTests(unittest.TestCase):
    def test_model_exposes_display_strings(self):
        src = MODEL.read_text(encoding="utf-8-sig")
        self.assertIn("QualityScoreText", src)
        self.assertIn("JitterText", src)
        # Unmeasured jitter (-1) must never be rendered as a raw -1.
        m = re.search(r"JitterText\s*=>\s*Jitter\s*>=\s*0\s*\?", src)
        self.assertIsNotNone(m, "JitterText must hide the -1 sentinel")
        self.assertIn("QualityScore > 0", src,
                      "QualityScoreText must hide an unmeasured score of 0")
        # A blank cell reads as a broken feature; a dash reads as "not measured".
        self.assertIn("NotMeasured", src,
                      "unmeasured quality cells should show a placeholder, not nothing")
        self.assertNotIn("string.Empty", m.group(0) if m else "x",
                         "the placeholder constant must be used for jitter too")

    def test_wpf_cells_bind_display_strings(self):
        src = WPF.read_text(encoding="utf-8-sig")
        self.assertIn('Binding="{Binding QualityScoreText}"', src)
        self.assertIn('Binding="{Binding JitterText}"', src)
        # Sorting must still use the numeric member.
        self.assertIn('SortMemberPath="QualityScore"', src)
        self.assertIn('SortMemberPath="Jitter"', src)

    def test_avalania_cells_bind_display_strings(self):
        src = AVLN.read_text(encoding="utf-8-sig")
        self.assertIn('Text="{Binding QualityScoreText}"', src)
        self.assertIn('Text="{Binding JitterText}"', src)

    def test_delay_only_tests_must_not_overwrite_a_real_measurement(self):
        """A plain delay or UDP probe has no jitter/loss, so it must not clobber.

        Passing PingQuality.None with the default isMeasured=true blanked the score
        and jitter every time the user ran the wrong test, which reads as the whole
        quality feature being broken.
        """
        src = SPEEDTEST.read_text(encoding="utf-8-sig")
        bare = re.findall(r"SetTestQuality\([^;]*PingQuality\.None\);", src)
        self.assertEqual(
            bare, [],
            "PingQuality.None must be passed with isMeasured: false; without it a "
            f"delay-only test overwrites the last real-ping result: {bare}",
        )
        # The real-ping call must stay authoritative (isMeasured defaults to true).
        measured = re.findall(r"SetTestQuality\(\s*(?:it|item)\.IndexId,\s*quality\s*\)", src)
        self.assertTrue(measured, "the real-ping call must still record its measurement")

    def test_set_test_quality_can_be_told_the_result_is_not_a_measurement(self):
        mgr = (ROOT / "v2rayN/ServiceLib/Manager/ProfileExManager.cs").read_text(
            encoding="utf-8-sig")
        self.assertRegex(
            mgr,
            r"SetTestQuality\(string indexId, PingQuality quality, bool isMeasured = true\)",
            "SetTestQuality needs an isMeasured flag so callers can decline to write",
        )
        self.assertIn("if (isMeasured)", mgr,
                      "an unmeasured result must not overwrite the stored fields")

    def test_every_delay_only_path_marks_quality_unmeasured(self):
        """Any test that writes a delay without a quality must say 'unmeasured'.

        Otherwise the row keeps whatever a previous run left behind, or shows a
        bare 0 that reads as a perfect score.
        """
        src = SPEEDTEST.read_text(encoding="utf-8-sig")
        self.assertEqual(
            calls(src, r"SetTestDelay\("),
            calls(src, r"SetTestQuality\("),
            "every SetTestDelay in SpeedtestService needs a matching SetTestQuality",
        )

    def test_quality_none_is_the_sentinel(self):
        src = (ROOT / "v2rayN/ServiceLib/Models/Dto/PingQuality.cs").read_text(
            encoding="utf-8-sig")
        self.assertIn("public static readonly PingQuality None", src)
        m = re.search(r"None = new\((-?\d+),", src)
        self.assertIsNotNone(m)
        self.assertEqual(int(m.group(1)), -1,
                         "None.Median must stay -1 to mean 'never measured'")
