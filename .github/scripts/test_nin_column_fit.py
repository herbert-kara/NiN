"""The profile grid must look right the moment the window opens.

WPF sizes an Auto column from its cells alone. Upstream's "autofit" set every
column to Auto, so a header with a short cell under it ("Delay (ms)" over "-1",
"Score" over "-1") was cut mid-word, the columns together stopped short of the
window and left a dead band on the right, the table never appeared to be laid
out - "قيافه اپ ريخته بهم". The fix fits the header as well as the content and
hands the leftover width to the widest column; this guard keeps a future merge
from quietly reverting it to the bare Auto loop.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VIEW = ROOT / "v2rayN/v2rayN/Views/ProfilesView.xaml.cs"
FIT = ROOT / "v2rayN/ServiceLib/Common/ColumnWidthFit.cs"


class NiNColumnFitTests(unittest.TestCase):
    def test_the_fit_measures_the_header_not_only_the_cells(self):
        src = VIEW.read_text(encoding="utf-8-sig")
        m = re.search(r"private void AutofitColumnWidth\(\).*?\n    \}", src, re.S)
        self.assertIsNotNone(m, "AutofitColumnWidth is missing")
        block = m.group(0)
        self.assertIn(
            "MeasureHeader",
            block,
            "an Auto-only fit clips every header whose cells are narrower than it")
        self.assertIn(
            "ColumnWidthFit.Fit",
            block,
            "the widths must come from the fitted content+header pair, not WPF's "
            "content-only Auto sizing")
        self.assertIn(
            "AvailableListWidth",
            block,
            "without the grid's width the columns cannot be spread to fill it")

    def test_the_header_allowance_covers_the_column_header_chrome(self):
        src = VIEW.read_text(encoding="utf-8-sig")
        m = re.search(r"private const double HeaderChrome = (\d+)", src)
        self.assertIsNotNone(
            m,
            "MaterialDesign pads the header cell and reserves room for the sort "
            "arrow: measuring the bare text still clips the last letters")
        self.assertGreaterEqual(
            int(m.group(1)), 30,
            "an allowance below the real header chrome re-introduces the clipping")

    def test_the_header_is_measured_from_the_rendered_element_first(self):
        """A text probe on its own left "Remarks" at 58px and trimmed it to "R.."."""
        src = VIEW.read_text(encoding="utf-8-sig")
        m = re.search(r"private double MeasureHeader\(DataGridColumn.*?\n    \}", src, re.S)
        self.assertIsNotNone(m, "MeasureHeader is missing")
        block = m.group(0)
        self.assertIn(
            "MeasureRealizedHeader",
            block,
            "the rendered header is the only place the real font, padding and sort "
            "arrow are all present; guessing the chrome from the text width is what "
            "left a header narrower than its own label")
        self.assertRegex(
            block,
            r"Math\.Max\(\s*measured",
            "a header scrolled out of view falls back to the text probe, so the "
            "wider of the two has to win")
        self.assertIn(
            "DataGridColumnHeader",
            src,
            "the realized header element is looked up through the visual tree")

    def test_the_fit_is_measured_synchronously(self):
        src = VIEW.read_text(encoding="utf-8-sig")
        m = re.search(r"private void AutofitColumnWidth\(\).*?\n    \}", src, re.S)
        block = m.group(0)
        self.assertIn(
            "UpdateLayout()",
            block,
            "reading ActualWidth before a layout pass returns the old widths; "
            "deferring the fit instead lets the grid draw at Auto and jump")
        for deferred in ("Task.Run", "BeginInvoke", "Dispatcher."):
            self.assertNotIn(
                deferred,
                block,
                "a deferred fit is what made the columns jump on every reload")

    def test_rows_are_fitted_as_soon_as_they_are_laid_out(self):
        src = VIEW.read_text(encoding="utf-8-sig")
        m = re.search(r"public void RefreshServersBiz\(\).*?\n    \}", src, re.S)
        self.assertIsNotNone(m, "RefreshServersBiz is missing")
        block = m.group(0)
        self.assertNotIn(
            "EnableAutoAdjustMainLvColWidth",
            block,
            "the setting is off by default: gating the first fit on it is how the "
            "app shipped a clipped grid out of the box")
        self.assertIn(
            "AutofitColumnWidth",
            block,
            "the fit has to actually run on the first layout, not only on the "
            "toolbar button and the first connect")
        self.assertIn(
            "_fittedToLayout",
            block,
            "refreshes also fire while a ping streams its results in: refitting "
            "each time makes the columns twitch as the delays arrive")

    def test_a_resize_refits_but_not_on_every_pixel(self):
        src = VIEW.read_text(encoding="utf-8-sig")
        m = re.search(r"private void LstProfiles_SizeChanged\(.*?\n    \}", src, re.S)
        self.assertIsNotNone(
            m,
            "a window that grew leaves the fitted columns short of its edge and "
            "the dead band is back")
        block = m.group(0)
        self.assertIn("EnableAutoAdjustMainLvColWidth", block,
                      "a manual column layout the user chose must survive a resize")
        self.assertIn("Items.Count == 0", block,
                      "fitting before any row exists budgets for headers alone and "
                      "then clips every long server name")
        threshold = re.search(r"Math\.Abs\(e\.NewSize\.Width - e\.PreviousSize\.Width\) < (\d+)", block)
        self.assertIsNotNone(threshold,
                             "refitting on every pixel of a drag makes the resize stick")
        self.assertGreaterEqual(int(threshold.group(1)), 8)

    def test_a_saved_layout_that_clips_its_headers_is_not_restored(self):
        """The broken widths were saved, so they came back on every single start."""
        src = VIEW.read_text(encoding="utf-8-sig")
        m = re.search(r"private void RestoreUI\(\).*?\n    \}", src, re.S)
        self.assertIsNotNone(m, "RestoreUI is missing")
        block = m.group(0)
        self.assertIn(
            "MeasureHeader",
            block,
            "a saved width narrower than its own header is a measurement artifact "
            "of the content-only fit, not a layout the user chose")
        self.assertRegex(
            block,
            r"_fittedToLayout = true",
            "a saved layout that IS readable must stand: refitting it anyway would "
            "throw away the columns the user dragged into place")

    def test_slack_goes_to_the_widest_column(self):
        src = FIT.read_text(encoding="utf-8-sig")
        self.assertIn(
            "Math.Max(contentWidths[i], headerWidths[i])",
            src,
            "the fitted width is the larger of content and header")
        self.assertRegex(
            src,
            r"widths\[widest\] \+= Math\.Floor",
            "the leftover width belongs to the widest column: leaving it unspent "
            "is the dead band beside the table")


if __name__ == "__main__":
    unittest.main()
