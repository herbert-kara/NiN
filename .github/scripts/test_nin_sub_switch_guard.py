# -*- coding: utf-8 -*-
"""Guard the subscription-switch re-entrancy fix.

Symptom: with many subscription groups, clicking between them highlights a
new group but the profile list never loads (stuck on an old list or blank).

Root cause: WhenAnyValue(SelectedSub).SubscribeAsync fires one async invocation
per selection. Clicking several subs in a row ran several
SubSelectedChangedAsync passes concurrently, each doing
config.SubIndexId = ... ; RefreshServers() ; focus. The last ReplaceRange could
land on a collection an earlier pass had already replaced, so the UI showed a
group highlight that no longer matched the rows, and the newer pass's own
replace raced with the older one's.

Fix: a pass counter + SemaphoreSlim(1,1). One switch at a time; a newer switch
abandons the older pass instead of letting both finish.
"""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
VM = ROOT / "v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs"
SELECT_VM = ROOT / "v2rayN/ServiceLib/ViewModels/ProfilesSelectViewModel.cs"


def body_of(src, marker):
    """Text from marker to the closing brace of its method body."""
    i = src.index(marker)
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


class NiNSubSwitchGuardTests(unittest.TestCase):
    def setUp(self):
        self.src = VM.read_text(encoding="utf-8-sig")

    def test_sub_switch_is_serialized(self):
        self.assertRegex(self.src, r"SemaphoreSlim\s+_subSwitchSemaphore\s*=\s*new\(\s*1\s*,\s*1\s*\)",
                         "The guard must allow exactly one switch at a time")
        body = body_of(self.src, "private async Task SubSelectedChangedAsync()")
        self.assertIn("_subSwitchSemaphore.WaitAsync()", body,
                      "The sub-switch body must actually take the guard, not just declare it")
        self.assertIn("_subSwitchSemaphore.Release()", body,
                      "The guard must be released so the next switch can run")

    def test_sub_switch_abandons_stale_passes(self):
        body = body_of(self.src, "private async Task SubSelectedChangedAsync()")
        self.assertIn("Interlocked.Increment(ref _subSwitchPass)", body,
                      "Each switch must stamp itself with a fresh pass id")
        self.assertIn("Volatile.Read(ref _subSwitchPass) != pass", body,
                      "A superseded switch must return early instead of finishing")

    def test_semaphore_always_released(self):
        body = body_of(self.src, "private async Task SubSelectedChangedAsync()")
        self.assertIn("finally", body,
                      "The guard must be released even when a pass is abandoned")
        self.assertIn("_subSwitchSemaphore.Release()", body,
                      "Release the sub-switch guard in the finally block")

    def test_no_stray_newer_pass_bypass(self):
        # The very last await (focus) must still be gated: a newer switch that
        # lands during the list rebuild must not have its focus stolen by the
        # abandoned pass.
        body = body_of(self.src, "private async Task SubSelectedChangedAsync()")
        checks = body.count("Volatile.Read(ref _subSwitchPass) != pass")
        self.assertGreaterEqual(checks, 2,
                                "Both the pre-work and the post-refresh path must check the pass")

    def test_refresh_uses_selected_sub_not_stale_config(self):
        body = body_of(self.src, "public async Task RefreshServersBiz()")
        self.assertIn("SelectedSub?.Id", body,
                      "The list must be rebuilt for the selected sub, not a stale config value")
        self.assertNotIn("GetProfileItemsEx(_config.SubIndexId", body,
                         "Reading _config.SubIndexId directly rebuilds the previous sub's list")

    def test_items_query_uses_its_subid_parameter(self):
        body = body_of(self.src, "private async Task<List<ProfileItemModel>?> GetProfileItemsEx")
        self.assertIn("ProfileModels(subid,", body,
                      "GetProfileItemsEx must query with the subid it was given")

    def test_empty_group_does_not_steal_a_server_from_another_sub(self):
        # Switching to an empty sub used to run a whole-table lookup and pick any
        # server from ANY sub, silently moving the connection to a group the user
        # did not select.
        ch = (ROOT / "v2rayN/ServiceLib/Handler/ConfigHandler.cs").read_text(encoding="utf-8-sig")
        body = body_of(ch, "public static async Task<int> SetDefaultServer(Config")
        self.assertIn("lstProfile.Count == 0", body,
                      "An empty list must be handled explicitly")
        self.assertNotIn("TableAsync<ProfileItem>().FirstOrDefaultAsync(t => t.Port > 0);", body,
                         "No whole-table server fallback: it moves the connection to another sub")

    def test_select_dialog_uses_selected_sub(self):
        # The server-select dialog has its own view model with the same stale-field
        # bug: _subIndexId is a constructor snapshot, so a fast switch or a
        # duplicate-named sub rebuilt the list for the previous group.
        src = SELECT_VM.read_text(encoding="utf-8-sig")
        body = body_of(src, "private async Task RefreshServersBiz()")
        self.assertIn("SelectedSub?.Id", body,
                      "The select dialog must rebuild for the selected sub, not the constructor snapshot")
        self.assertNotIn("GetProfileItemsEx(_subIndexId,", body,
                         "_subIndexId is a constructor snapshot: it holds the previous sub")

    def test_select_dialog_items_query_uses_its_parameter(self):
        src = SELECT_VM.read_text(encoding="utf-8-sig")
        body = body_of(src, "private async Task<List<ProfileItemModel>?> GetProfileItemsEx")
        self.assertIn("ProfileModels(subid,", body,
                      "GetProfileItemsEx must query with the subid it was given")

    def test_both_queries_project_quality(self):
        # The select dialog projects its rows from the same ProfileExItem table, but
        # its projection omitted Jitter/QualityScore, so its Score/Jitter columns
        # were permanently empty no matter how many real-ping runs happened.
        for label, path in (("main list", VM), ("select dialog", SELECT_VM)):
            src = path.read_text(encoding="utf-8-sig")
            body = body_of(src, "private async Task<List<ProfileItemModel>?> GetProfileItemsEx")
            self.assertIn("Jitter = t33?.Jitter ?? -1", body,
                          f"{label}: Jitter must come from the ProfileExItem row")
            self.assertIn("QualityScore = t33?.QualityScore ?? 0", body,
                          f"{label}: QualityScore must come from the ProfileExItem row")
            self.assertIn("LossVal = t33 == null ? -1", body,
                          f"{label}: LossVal must be projected, -1 when never measured")

    def test_unmeasured_test_never_blanks_quality(self):
        # Auto-refresh runs tcping, and tcping used to reset Jitter to -1 even after a
        # real-ping run had measured it. That is what made the Score/Jitter columns
        # fall back to "--" a few seconds into a refresh loop.
        src = (ROOT / "v2rayN/ServiceLib/Manager/ProfileExManager.cs").read_text(encoding="utf-8-sig")
        body = body_of(src, "public void SetTestQuality")
        self.assertNotIn("profileEx.Jitter = -1", body,
                         "An unmeasured test must leave the measured Jitter alone")
        self.assertNotIn("else if (profileEx.QualityScore == 0)", body,
                         "No unmeasured branch may write quality columns")
        self.assertNotIn("profileEx.PacketLoss = quality.Loss;", body.split("IndexIdEnqueue(indexId)")[-1],
                         "Only a measured run may write quality columns")
        # Both unmeasured callers must pass isMeasured: false.
        svc = (ROOT / "v2rayN/ServiceLib/Services/SpeedtestService.cs").read_text(encoding="utf-8-sig")
        self.assertIn("SetTestQuality(item.IndexId, PingQuality.None, false)", svc,
                      "Tcping must not claim a measurement")
        self.assertIn("SetTestQuality(it.IndexId, PingQuality.None, false)", svc,
                      "The UDP probe must not claim a measurement")


if __name__ == "__main__":
    unittest.main()
