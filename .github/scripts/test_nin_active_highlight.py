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
CONFIG_HANDLER = ROOT / "v2rayN/ServiceLib/Handler/ConfigHandler.cs"


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

    def test_the_highlight_follows_the_connection_not_the_click(self):
        # IsActive means "this row is the live connection", and config.IndexId decides
        # that. Deriving it from SelectedProfile lit up whatever row the user clicked --
        # including the "active" label bound to it -- while the real connection stayed
        # unmarked, which is exactly what the user reported.
        src = VM.read_text(encoding="utf-8-sig")
        m = re.search(
            r"WhenAnyValue\(x => x\.SelectedProfile\)\s*\n(?:\s*\.\w+\([^\n]*\)\s*\n)*\s*\.Subscribe\(item =>(.{0,700})",
            src, re.S)
        self.assertIsNotNone(m, "the SelectedProfile handler is missing")
        block = m.group(1)
        self.assertIn("IsActive", block,
                      "changing the selection must reassign IsActive on the rows")
        self.assertRegex(block, r"row\.IsActive\s*=",
                         "the flag has to be assigned, not just read")
        self.assertRegex(
            block,
            r"row\.IsActive\s*=\s*!indexId\.IsNullOrEmpty\(\)\s*&&\s*"
            r"row\.IndexId\s*==\s*indexId",
            "IsActive must come from config.IndexId, not from the selected item: "
            "comparing against item.IndexId marks whichever row was clicked")

    def test_exactly_one_row_can_be_active(self):
        """The old row must be cleared, not just the new one marked."""
        src = VM.read_text(encoding="utf-8-sig")
        m = re.search(
            r"WhenAnyValue\(x => x\.SelectedProfile\)\s*\n(?:\s*\.\w+\([^\n]*\)\s*\n)*\s*\.Subscribe\(item =>(.{0,600})",
            src, re.S)
        block = m.group(1)
        self.assertIn("foreach", block, "every row has to be visited, not just the new one")
        # The flag must be a straight equality against the CONNECTED IndexId. Anything
        # that compares against the selected item instead lights up the clicked row,
        # which is the bug the user hit: "active" appeared on every row they clicked.
        self.assertNotRegex(
            block, r"row\.IndexId\s*==\s*item\.IndexId",
            "comparing against the selected item marks the clicked row, not the "
            "connected one -- compare against config.IndexId")

    def test_the_grid_binds_to_is_active(self):
        # The trigger must sit on the DataGrid ROW. On the cell it lost: an implicit
        # DataGridCell style is overridden by any column that styles itself, and the
        # active highlight silently vanished. The row is every cell's parent and no
        # column can override it.
        for path in (XAML, ROOT / "v2rayN/v2rayN/Views/ProfilesSelectWindow.xaml"):
            src = path.read_text(encoding="utf-8-sig")
            # A DataGridCell style may exist -- it is what stops a selected cell from
            # repainting the row's active background -- but it must not paint a solid
            # colour of its own, which would cover the highlight again.
            for m in re.finditer(r'TargetType="DataGridCell"[^>]*>(.*?)</Style>', src, re.S):
                self.assertNotRegex(
                    m.group(1), r'Property="Background" Value="(?!Transparent)',
                    f"{path.name}: a DataGridCell style must leave Background transparent")
            m = re.search(
                r'TargetType="DataGridRow"[^>]*>(.*?)</Style>', src, re.S)
            self.assertIsNotNone(m, f"{path.name}: the row style is missing")
            self.assertRegex(
                m.group(1), r'DataTrigger Binding="\{Binding IsActive\}" Value="True"',
                f"{path.name}: the IsActive trigger belongs on the row style")

    def test_every_active_server_change_announces_itself(self):
        # SetDefaultServerIndex is the single choke point for "this server is now the
        # active one": the grid's own command, a subscription import, and two
        # ConfigHandler fallbacks all route through it. When only the first caller
        # refreshed the list, the highlight stayed on the old row for every other path
        # -- which is what the user saw (row 1 active, looking like the rest).
        src = CONFIG_HANDLER.read_text(encoding="utf-8-sig")
        m = re.search(
            r"public static async Task<int> SetDefaultServerIndex\(Config config, "
            r"string\? indexId\)(.{0,1200}?)\n    \}",
            src, re.S)
        self.assertIsNotNone(m, "SetDefaultServerIndex is missing")
        body = m.group(1)
        self.assertRegex(
            body, r"config\.IndexId = indexId;",
            "SetDefaultServerIndex must assign the active server")
        self.assertRegex(
            body, r"ActiveServerChanged\.Publish",
            "SetDefaultServerIndex must announce the change: every caller writes "
            "config.IndexId, and only an announcement lets the grid re-assert the "
            "highlight for callers that do not refresh the list themselves")
        # An early return before the publish would silently reintroduce the bug for
        # the no-op case, so the assignment must come first.
        self.assertLess(
            body.index("config.IndexId = indexId;"),
            body.index("ActiveServerChanged.Publish"),
            "the change must be applied before it is announced")
        # The reload channel restarts the core and cuts the traffic, which is what the
        # user reported as "the connection hangs". The highlight needs its own channel.
        self.assertNotRegex(
            body, r"ReloadRequested\.Publish",
            "announcing an active-server change on the reload channel restarts the "
            "core and cuts the traffic -- use ActiveServerChanged instead")

    def test_the_grid_reacts_to_the_announcement(self):
        # Announcing is only half of it: the grid has to listen. Config is a plain
        # serializable class with no change notification, so a WhenAnyValue on
        # config.IndexId would never fire -- the subscription must be on the channel.
        src = VM.read_text(encoding="utf-8-sig")
        self.assertIn("ReassertActiveRow", src,
                      "the grid needs a handler that re-asserts the highlight")
        # Anchor on the SUBSCRIPTION (the last occurrence), not the method definition:
        # what matters is that the grid wires the handler to the channel, not merely
        # that a method by that name exists.
        m = None
        for m in re.finditer(r"Subscribe\(_ => ReassertActiveRow\(\)\)", src):
            pass
        self.assertIsNotNone(m, "nothing subscribes to ReassertActiveRow")
        window = src[max(0, m.start() - 600):m.end() + 200]
        self.assertIn("ActiveServerChanged", window,
                      "the grid must listen on ActiveServerChanged")
        self.assertNotIn(
            "WhenAnyValue(x => x._config.IndexId", window,
            "Config has no change notification, so binding to config.IndexId would "
            "never fire -- subscribe to the announcement channel instead")
        # Re-asserting the highlight must not reload the list either: that would
        # restart the core and cut the traffic.
        self.assertNotIn("ReloadRequested", window,
                         "the highlight must not be wired to the reload channel")

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
