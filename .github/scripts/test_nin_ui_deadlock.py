"""The flag lookup must never block the UI thread.

RefreshServersBiz runs inside a blocking Dispatcher.Invoke, and the lookup
marshals its results back onto that same UI thread. Awaiting the lookup from
there is a deadlock: the thread waits for the lookup, the lookup waits for the
thread. With a large subscription it is not a stall but a permanent freeze -- the
user cannot switch configs or subscription groups until the app is restarted.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VM = ROOT / "v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs"
VIEW = ROOT / "v2rayN/v2rayN/Views/ProfilesView.xaml.cs"


class NiNUiDeadlockTests(unittest.TestCase):
    def test_refresh_does_not_await_the_lookup(self):
        src = VM.read_text(encoding="utf-8-sig")
        tail = src.split("RefreshServersBiz()", 1)[1]
        m = re.search(r"(await\s+)?LookupServerFlagsAsync\(", tail[:2400])
        self.assertIsNotNone(m, "RefreshServersBiz must still start the lookup")
        self.assertIsNone(
            m.group(1),
            "awaiting LookupServerFlagsAsync here deadlocks the UI thread; fire it "
            "and forget, the results patch the live rows when they arrive",
        )

    def test_the_dispatcher_call_is_really_blocking(self):
        """Pin the assumption the guard above rests on."""
        src = VIEW.read_text(encoding="utf-8-sig")
        start = src.index("DispatcherRefreshServersBizInteraction.RegisterHandler")
        block = src[start:start + 400]
        self.assertIn("Dispatcher.Invoke", block,
                      "Dispatcher.Invoke blocks until the callback returns")

    def test_a_newer_pass_abandons_the_older_one(self):
        """Switching groups must not leave the previous pass still querying."""
        src = VM.read_text(encoding="utf-8-sig")
        self.assertIn("_flagLookupPass", src)
        self.assertRegex(src, r"Interlocked\.Increment\(ref _flagLookupPass\)",
                         "each pass must take a fresh id")
        self.assertRegex(src, r"Volatile\.Read\(ref _flagLookupPass\)\s*!=\s*pass",
                         "the pass must bail out when a newer one started")
