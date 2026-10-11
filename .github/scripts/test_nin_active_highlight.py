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
    def test_is_active_notifies_the_grid(self):
        src = MODEL.read_text(encoding="utf-8-sig")
        # [Reactive] partial needs the source generator this project does not build,
        # so it compiles to a silent property: the highlight would never move.
        self.assertNotRegex(
            src, r"\[Reactive\]\s*\n\s*public partial bool IsActive",
            "IsActive must not be a [Reactive] partial: the generator emits a second "
            "definition (CS0102) and the grid never sees the change")
        # The backing field must stay plain too: a [Reactive] on the field makes the
        # generator emit a duplicate IsActive and the build fails.
        self.assertNotRegex(
            src, r"\[Reactive\]\s*\n\s*private bool _isActive",
            "the backing field must not be [Reactive]: the generator would emit a "
            "duplicate IsActive definition")
        self.assertRegex(
            src,
            r"public bool IsActive\s*\{\s*get\s*=>\s*_isActive;\s*set\s*=>\s*"
            r"this\.RaiseAndSetIfChanged\(ref _isActive, value\);\s*\}",
            "IsActive must raise PropertyChanged through RaiseAndSetIfChanged")

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
        # The trigger must sit on the DataGrid ROW. On the cell it lost: an implicit
        # DataGridCell style is overridden by any column that styles itself, and the
        # active highlight silently vanished. The row is every cell's parent and no
        # column can override it.
        for path in (XAML, ROOT / "v2rayN/v2rayN/Views/ProfilesSelectWindow.xaml"):
            src = path.read_text(encoding="utf-8-sig")
            self.assertNotRegex(
                src, r'TargetType="DataGridCell"',
                f"{path.name}: no DataGridCell style may carry the highlight")
            m = re.search(
                r'TargetType="DataGridRow"[^>]*>(.*?)</Style>', src, re.S)
            self.assertIsNotNone(m, f"{path.name}: the row style is missing")
            self.assertRegex(
                m.group(1), r'DataTrigger Binding="\{Binding IsActive\}" Value="True"',
                f"{path.name}: the IsActive trigger belongs on the row style")

    def test_switching_server_resyncs_selected_profile(self):
        # RefreshServers replaces every row object, so a SelectedProfile captured
        # before the refresh points at a row that is no longer in the grid: the
        # highlight kept sitting on the dead object and never moved.
        vm = VM.read_text(encoding="utf-8-sig")
        i = vm.index("public async Task SetDefaultServer(string? indexId)")
        body = vm[i:vm.index("\n    }", i)]
        self.assertRegex(body, r"SelectedProfile\s*=\s*ProfileItems\.FirstOrDefault\(",
                         "SelectedProfile must be re-pointed at the live row")
        # The assignment has to come AFTER the refresh, or it points at dead objects.
        self.assertLess(body.index("await RefreshServers();"),
                        body.index("SelectedProfile ="),
                        "sync SelectedProfile after the refresh, not before")


if __name__ == "__main__":
    unittest.main()
