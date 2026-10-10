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


if __name__ == "__main__":
    unittest.main()
