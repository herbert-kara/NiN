"""The blue row highlight must follow the active connection.

IsActive marks which row is the live connection. It used to be a plain property
computed only when the list was rebuilt from config, so the highlight stayed on
the old row until the app restarted even though traffic had really moved.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VM = ROOT / "v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs"
MODEL = ROOT / "v2rayN/ServiceLib/Models/Dto/ProfileItemModel.cs"
XAML = ROOT / "v2rayN/v2rayN/Views/ProfilesView.xaml"


class NiNActiveHighlightTests(unittest.TestCase):
    def test_is_active_is_reactive(self):
        src = MODEL.read_text(encoding="utf-8-sig")
        m = re.search(r"\[Reactive\]\s*\n\s*public partial bool IsActive", src)
        self.assertIsNotNone(
            m,
            "IsActive must be a [Reactive] partial property, otherwise setting it "
            "never notifies the grid and the highlight cannot move",
        )

    def test_selection_moves_the_highlight(self):
        src = VM.read_text(encoding="utf-8-sig")
        m = re.search(
            r"WhenAnyValue\(x => x\.SelectedProfile\)\s*\n\s*\.Subscribe\(item =>(.{0,600})",
            src, re.S)
        self.assertIsNotNone(m, "the SelectedProfile handler is missing")
        block = m.group(1)
        self.assertIn("IsActive", block,
                      "changing the selection must reassign IsActive on the rows")
        self.assertRegex(block, r"row\.IsActive\s*=",
                         "the flag has to be assigned, not just read")

    def test_exactly_one_row_can_be_active(self):
        """The old row must be cleared, not just the new one marked."""
        src = VM.read_text(encoding="utf-8-sig")
        m = re.search(
            r"WhenAnyValue\(x => x\.SelectedProfile\)\s*\n\s*\.Subscribe\(item =>(.{0,600})",
            src, re.S)
        block = m.group(1)
        self.assertIn("foreach", block, "every row has to be visited, not just the new one")
        # The flag must be a straight equality against the selected IndexId. Anything
        # that inverts the logic (`==` with a null guard on the wrong side) lights up
        # every row instead of exactly one.
        self.assertRegex(
            block,
            r"row\.IsActive\s*=\s*item\s*!=\s*null\s*&&\s*row\.IndexId\s*==\s*item\.IndexId",
            "IsActive must be `item != null && row.IndexId == item.IndexId` so exactly "
            "one row lights up; an inverted comparison lights up all of them",
        )

    def test_the_grid_binds_to_is_active(self):
        src = XAML.read_text(encoding="utf-8-sig")
        self.assertRegex(src, r'DataTrigger Binding="\{Binding IsActive\}" Value="True"',
                         "the highlight is a DataTrigger on IsActive")


if __name__ == "__main__":
    unittest.main()
