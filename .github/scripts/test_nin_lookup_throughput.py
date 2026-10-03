"""The reputation lookup must not serialise a whole subscription.

With one request in flight and a 1100 ms gap between requests, decorating a
190-config subscription took about three and a half minutes -- that is the UI
hang. proxycheck.io's free plan allows 1000 queries a minute, so the gate has to
let requests overlap.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SVC = ROOT / "v2rayN/ServiceLib/Services/ServerFlaggedService.cs"


class NiNLookupThroughputTests(unittest.TestCase):
    def src(self):
        return SVC.read_text(encoding="utf-8-sig")

    def test_gate_allows_overlap(self):
        src = self.src()
        m = re.search(r"_httpGate\s*=\s*new\((\d+),\s*(\d+)\)", src)
        self.assertIsNotNone(m, "the concurrency gate moved; update this guard")
        limit = int(m.group(1))
        self.assertGreater(
            limit, 1,
            "a single-slot gate makes every config wait for the previous request, "
            "which is what made a large subscription appear to hang",
        )

    def test_gap_is_not_serialisation(self):
        src = self.src()
        m = re.search(r"RequestGap\s*=\s*TimeSpan\.FromMilliseconds\((\d+)\)", src)
        self.assertIsNotNone(m)
        gap = int(m.group(1))
        self.assertLess(
            gap, 1000,
            "a gap near a second between starts serialises the pass; the gate "
            "already bounds concurrency",
        )
        self.assertGreater(
            gap, 0,
            "a zero gap drops the courtesy spacing entirely and turns 50 concurrent "
            "senders into a burst the provider may refuse",
        )

    def test_gap_accounting_is_thread_safe(self):
        """50 writers now touch the shared timestamp, so a plain write is a race."""
        src = self.src()
        self.assertIn("Interlocked.Exchange(ref _lastRequestTicks", src,
                      "with concurrent senders the tick must be written atomically")
        self.assertNotRegex(src, r"^\s*_lastRequestTicks\s*=\s*Stopwatch",
                            "a plain write to the shared tick races under the gate")

    def test_a_large_subscription_finishes_in_bounded_time(self):
        """Pin the actual user-visible number: how long the first paint waits.

        The gate holds `slots` requests and each start is spaced by `gap`, so the
        whole pass is roughly `slots * gap` for the first `slots` requests and then
        one request per gap for the rest. A 190-config subscription should be
        seconds, not minutes.
        """
        src = self.src()
        gap = int(re.search(r"RequestGap\s*=\s*TimeSpan\.FromMilliseconds\((\d+)\)",
                            src).group(1))
        slots = int(re.search(r"_httpGate\s*=\s*new\((\d+),", src).group(1))

        configs = 190
        seconds = max(slots, configs) * gap / 1000
        self.assertLess(
            seconds, 30,
            f"{configs} configs at {slots} slots / {gap}ms is about "
            f"{seconds:.0f}s of delay, which reads as a hang",
        )
