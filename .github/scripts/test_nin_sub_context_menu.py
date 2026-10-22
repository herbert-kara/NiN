"""Right-clicking a subscription group must open a menu that acts on that group.

The chips at the top of the Configuration tab are the subscription groups. The
Avalonia build had a context menu on them; the WPF build did not, so on Windows
right-clicking a group did nothing at all -- there was no way to edit or delete
a group, and the user had to give up and use the buttons instead.

Two things have to hold, and the second one matters more than the first:

  1. The WPF build must have the menu, like the Avalonia build.
  2. The menu must act on the chip that was RIGHT-CLICKED, not on whichever
     group happened to be selected. Several groups can share a name, and
     deleting a group deletes the servers in it, so acting on the wrong one
     is not recoverable.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WPF_XAML = ROOT / "v2rayN/v2rayN/Views/ProfilesView.xaml"
WPF_CODE = ROOT / "v2rayN/v2rayN/Views/ProfilesView.xaml.cs"
AVALONIA_XAML = ROOT / "v2rayN/v2rayN.Desktop/Views/ProfilesView.axaml"
VM = ROOT / "v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs"


class NiNSubGroupContextMenuTests(unittest.TestCase):
    def test_the_wpf_chips_have_a_context_menu(self):
        src = WPF_XAML.read_text(encoding="utf-8-sig")
        m = re.search(r"<ListBox\b[^>]*x:Name=\"lstGroup\".*?</ListBox>", src, re.S)
        self.assertIsNotNone(m, "the subscription group chip list is missing")
        block = m.group(0)
        self.assertIn(
            "<ListBox.ContextMenu>",
            block,
            "the WPF chip list has no context menu: right-clicking a group did "
            "nothing, so there was no way to edit or delete one")
        for item in ("menuSubEdit", "menuSubAdd", "menuSubDelete"):
            self.assertIn(item, block, f"the chip menu is missing {item}")

    def test_the_menu_is_wired_to_the_commands(self):
        src = WPF_CODE.read_text(encoding="utf-8-sig")
        for cmd, item in (
            ("EditSubCmd", "menuSubEdit"),
            ("AddSubCmd", "menuSubAdd"),
            ("DeleteSubCmd", "menuSubDelete"),
        ):
            self.assertRegex(
                src,
                rf"BindCommand\(ViewModel, vm => vm\.{cmd}, v => v\.{item}\)",
                f"{item} must be bound to {cmd}, or the menu does nothing")

    def test_a_right_click_selects_the_chip_under_the_cursor(self):
        # WPF does not move the selection on a right-click. Without this the
        # menu would act on the selected group, which is a different one.
        src = WPF_CODE.read_text(encoding="utf-8-sig")
        self.assertRegex(
            src,
            r"lstGroup\.PreviewMouseRightButtonDown\s*\+=",
            "the chip list must watch for a right-click")
        m = re.search(
            r"private void LstGroup_PreviewMouseRightButtonDown\(.*?\n    \}", src, re.S)
        self.assertIsNotNone(m, "the right-click handler is missing")
        block = m.group(0)
        self.assertRegex(
            block,
            r"lstGroup\.SelectedItem\s*=",
            "the right-click handler must select the chip that was hit, or the "
            "menu acts on a different group")
        self.assertRegex(
            block,
            r"ItemsControl\.ContainerFromElement\(",
            "the handler must find the chip that was hit")
        self.assertRegex(
            block,
            r"DataContext is SubItem ",
            "the handler must read the SubItem the chip holds: it is the group's "
            "identity, and the chip list has nothing else")

    def test_the_actions_target_the_selected_group_not_the_config(self):
        # _config.SubIndexId lags behind the selection: the group switch is
        # asynchronous, so it still names the previous group for a moment.
        # Both the edit and the delete must resolve their target the same way.
        src = VM.read_text(encoding="utf-8-sig")
        m = re.search(r"private async Task<SubItem\?> GetContextSub\(\).*?\n    \}", src, re.S)
        self.assertIsNotNone(m, "the shared target resolver is missing")
        block = m.group(0)
        self.assertIn(
            "SelectedSub?.Id",
            block,
            "the actions must target the group the user pointed at")
        self.assertNotIn(
            "_config.SubIndexId",
            block,
            "the actions must not target config.SubIndexId: it names the previous "
            "group until the switch finishes, so the wrong group would be deleted")

        for method in ("DeleteSubAsync", "EditSubAsync"):
            mm = re.search(rf"private async Task {method}\(.*?\n    \}}", src, re.S)
            self.assertIsNotNone(mm, f"{method} is missing")
            self.assertIn(
                "GetContextSub()",
                mm.group(0),
                f"{method} must resolve its target through GetContextSub, or it "
                "can act on a different group than the one that was clicked")

    def test_the_all_group_cannot_be_deleted(self):
        # The first chip is a synthetic "All" entry with no id; it is not a row
        # in the database and must not be deletable.
        src = VM.read_text(encoding="utf-8-sig")
        m = re.search(r"private async Task<SubItem\?> GetContextSub\(\).*?\n    \}", src, re.S)
        self.assertIsNotNone(m, "the shared target resolver is missing")
        self.assertRegex(
            m.group(0),
            r"id\.IsNullOrEmpty\(\)",
            "the actions must refuse the synthetic All entry, which has no id")

    def test_the_avalonia_build_still_has_its_menu(self):
        src = AVALONIA_XAML.read_text(encoding="utf-8-sig")
        self.assertIn("<ListBox.ContextMenu>", src,
                      "the Avalonia chip menu must not be lost")


if __name__ == "__main__":
    unittest.main()
