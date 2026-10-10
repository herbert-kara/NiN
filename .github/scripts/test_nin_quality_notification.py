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


def setter_of(src, prop):
    """Text of the setter block of `public int <prop>` in src, or None."""
    key = f"public int {prop}"
    i = src.index(key)
    i = src.index("{", i)
    depth, j = 0, i
    while True:
        c = src[j]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return src[i:j + 1]
        j += 1


class NiNQualityNotificationTests(unittest.TestCase):
    def setUp(self):
        self.model = MODEL.read_text(encoding="utf-8-sig")
        self.xaml = XAML.read_text(encoding="utf-8")
        self.service = SERVICE.read_text(encoding="utf-8-sig")

    def test_jitter_setter_raises_jitter_text(self):
        block = setter_of(self.model, "Jitter")
        self.assertIsNotNone(block, "Jitter must be a property with a setter")
        self.assertIn("RaiseAndSetIfChanged(ref _jitter,", block,
                      "Jitter setter must notify via RaiseAndSetIfChanged")
        self.assertIn("RaisePropertyChanged(nameof(JitterText))", block,
                      "Jitter setter must notify JitterText, not just Jitter")

    def test_score_setter_raises_score_text(self):
        block = setter_of(self.model, "QualityScore")
        self.assertIsNotNone(block, "QualityScore must be a property with a setter")
        self.assertIn("RaiseAndSetIfChanged(ref _qualityScore,", block,
                      "QualityScore setter must notify via RaiseAndSetIfChanged")
        self.assertIn("RaisePropertyChanged(nameof(QualityScoreText))", block,
                      "QualityScore setter must notify QualityScoreText, not just QualityScore")

    def test_no_partial_change_hooks(self):
        # ReactiveUI.SourceGenerators here does not emit OnXxxChanged defining
        # declarations; a bare partial void hook fails the build (CS0759).
        self.assertNotIn("partial void OnJitterChanged", self.model,
                         "CS0759: no defining declaration for a partial OnXxxChanged hook")
        self.assertNotIn("partial void OnQualityScoreChanged", self.model,
                         "CS0759: no defining declaration for a partial OnXxxChanged hook")

    def test_jitter_defaults_to_unmeasured(self):
        self.assertIn("private int _jitter = -1;", self.model,
                      "Jitter must default to -1 so an untested row shows the em dash")

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
        # A retry that never reached the tunnel reports Median<0. Guard the store
        # on a successful response or the batch retry blanks a measured result.
        self.assertIn("if (responseTime > 0)", block,
                      "A failed retry (Median<0) must not overwrite a measured quality result")

    def test_delay_test_does_not_erase_quality(self):
        # Tcping/UDP pass None with overwrite=false; only the measured path
        # may set quality with overwrite.
        self.assertIn("SetTestQuality(it.IndexId, PingQuality.None, false)", self.service,
                      "Non-real-ping tests must not overwrite a measured quality result")


if __name__ == "__main__":
    unittest.main()
