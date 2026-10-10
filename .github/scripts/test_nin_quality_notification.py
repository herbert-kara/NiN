# -*- coding: utf-8 -*-
"""Guard the quality display pipeline end to end.

Two independent bugs hid here before:

1. The grid binds to QualityScoreText/JitterText, but those were plain
   computed getters -- when the live row's QualityScore/Jitter changed,
   PropertyChanged fired for the value properties only, never for the
   *Text properties, so the cell kept rendering the initial dash forever.
   Values appeared only after a full list rebuild (restart/group switch),
   which read as "jitter/score never works".

2. A quality run stores quality in ProfileExManager; if the real-ping path
   stops calling SetTestQuality, the same dash shows again.
"""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
MODEL = ROOT / "v2rayN/ServiceLib/Models/Dto/ProfileItemModel.cs"
XAML = ROOT / "v2rayN/v2rayN/Views/ProfilesView.xaml"
SERVICE = ROOT / "v2rayN/ServiceLib/Services/SpeedtestService.cs"


class NiNQualityNotificationTests(unittest.TestCase):
    def setUp(self):
        self.model = MODEL.read_text(encoding="utf-8-sig")
        self.xaml = XAML.read_text(encoding="utf-8")
        self.service = SERVICE.read_text(encoding="utf-8")

    def test_jitter_change_raises_jitter_text(self):
        self.assertIn("partial void OnJitterChanged", self.model,
                      "Jitter changes must raise notification for JitterText")
        block = self.model[self.model.index("partial void OnJitterChanged"):]
        block = block[:block.index("}")]
        self.assertIn("RaisePropertyChanged(nameof(JitterText))", block,
                      "OnJitterChanged must notify JitterText, not just Jitter")

    def test_score_change_raises_score_text(self):
        self.assertIn("partial void OnQualityScoreChanged", self.model,
                      "QualityScore changes must raise notification for QualityScoreText")
        block = self.model[self.model.index("partial void OnQualityScoreChanged"):]
        block = block[:block.index("}")]
        self.assertIn("RaisePropertyChanged(nameof(QualityScoreText))", block,
                      "OnQualityScoreChanged must notify QualityScoreText, not just QualityScore")

    def test_grid_binds_to_text_properties(self):
        self.assertIn('Binding="{Binding QualityScoreText}"', self.xaml,
                      "Score column must bind to QualityScoreText")
        self.assertIn('Binding="{Binding JitterText}"', self.xaml,
                      "Jitter column must bind to JitterText")

    def test_real_ping_stores_quality(self):
        block = self.service[self.service.index("private async Task<int> DoRealPing"):]
        nxt = block.find("private async Task", 10)
        if nxt > 0:
            block = block[:nxt]
        self.assertIn("SetTestQuality(it.IndexId, quality)", block,
                      "Real ping must store quality for the row, not just delay")

    def test_delay_test_does_not_erase_quality(self):
        # Tcping/UDP pass None with overwrite=false; only the measured path
        # may set quality with overwrite.
        self.assertIn("SetTestQuality(it.IndexId, PingQuality.None, false)", self.service,
                      "Non-real-ping tests must not overwrite a measured quality result")


if __name__ == "__main__":
    unittest.main()
