"""A tunnel that ran and then died must be started again once, not take the whole connection down.

On this box the WARP path resets from time to time: the session core exited with code -1 after some
fifty seconds and CoreManager stopped Xray and the system proxy with it, so the user lost the
connection until they restarted the app. The session core now gets one more start on the same
arguments and the same generation, and only a second death ends the session. This guard keeps a
future merge from quietly going back to stopping everything on the first exit.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANAGER = ROOT / "v2rayN/ServiceLib/Aether/AetherCoreManager.cs"
CORE_MANAGER = ROOT / "v2rayN/ServiceLib/Manager/CoreManager.cs"


def body(src, signature):
    m = re.search(re.escape(signature) + r".*?\n    \}", src, re.S)
    return m.group(0) if m else None


class NiNAetherRestartTests(unittest.TestCase):
    def setUp(self):
        self.src = MANAGER.read_text(encoding="utf-8-sig")

    def test_the_restart_is_bounded_and_only_for_a_core_that_ran(self):
        watch = body(self.src, "private async Task WatchAsync(")
        self.assertIsNotNone(watch, "WatchAsync is missing")
        self.assertIn("session.Restarts < MaxSessionRestarts", watch,
                      "without a bound a broken path keeps the cores starting forever")
        self.assertIn("session.Lifetime.Elapsed >= StartWindow", watch,
                      "a core that never got going must be reported, not started again")
        self.assertIn("!CouldNotStart(", watch,
                      "a missing DLL is no reason to spawn the same core again")
        self.assertRegex(self.src, r"public const int MaxSessionRestarts = 1;",
                         "one restart: enough for a path that reset, not a loop")

    def test_the_count_stays_with_the_session(self):
        self.assertIn("internal int Restarts { get; set; }", self.src,
                      "a new session per restart would reset the count and breed restarts forever")
        watch = body(self.src, "private async Task WatchAsync(")
        self.assertIn("session.Restarts++;", watch)

    def test_the_restart_keeps_the_session_and_its_generation(self):
        restart = body(self.src, "private async Task<string?> RestartAsync(")
        self.assertIsNotNone(restart, "RestartAsync is missing")
        self.assertIn("if (_session != session || session.StopRequested)", restart,
                      "a stop or a start that came meanwhile owns the session; the restart must give way")
        self.assertIn("return Spawn(session);", restart,
                      "the same session is spawned again, so Xray's outbounds and the cores' turn keep pointing at it")
        self.assertNotIn("StartSessionAsync(", restart,
                         "a new session would take a new generation and leave the cores' turn behind")

    def test_what_the_dead_core_started_is_ended_before_the_new_one_binds(self):
        watch = body(self.src, "private async Task WatchAsync(")
        self.assertIn("if (message != null || restart)", watch,
                      "the restart path must end the old core's children too: Psiphon holds the listener "
                      "and the datastore lock, and the new core cannot bind over them")
        self.assertIn("EndProcessAsync(process", watch)

    def test_the_new_core_needs_its_own_word(self):
        reset = body(self.src, "internal void ResetForRestart()")
        self.assertIsNotNone(reset, "ResetForRestart is missing")
        self.assertIn("_wordSeen = !NeedsWord;", reset,
                      "readiness waits for the new core's word, not the dead one's")
        self.assertIn("_lastError = null;", reset,
                      "the old core's last error is no reason to stop the new one")
        self.assertIn("_chainEnd = null;", reset)

    def test_a_real_end_still_stops_the_cores(self):
        src = CORE_MANAGER.read_text(encoding="utf-8-sig")
        self.assertIn("the Aether core exited while running, stopping the cores", src,
                      "after the restart is spent the cores still stop together, so traffic fails "
                      "rather than leaving another way")


if __name__ == "__main__":
    unittest.main()
