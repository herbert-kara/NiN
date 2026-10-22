"""A wedged tunnel must recover on its own, without the user pressing Reload.

The user reported the app hanging: traffic stopped flowing, and only a manual
"real delay" run brought it back. This guard pins the three rules that keep the
watchdog from making things worse:

  1. It only fires on a tunnel that USED to work. A server that is down, or an
     app that was never connected, cannot be fixed by reloading the config.
  2. It reloads through the same path as the Reload button, so a wedged tunnel
     is recovered exactly the way a manual reload recovers it.
  3. It does not reload twice for the same stall, and it does not reload while
     the core is still coming up after a reload.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WATCHDOG = ROOT / "v2rayN/ServiceLib/Services/TrafficWatchdog.cs"
SERVICE = ROOT / "v2rayN/ServiceLib/Services/TrafficWatchdogService.cs"
MAIN_VM = ROOT / "v2rayN/ServiceLib/ViewModels/MainWindowViewModel.cs"


class NiNTrafficWatchdogTests(unittest.TestCase):
    def test_the_watchdog_exists_with_its_state_machine(self):
        self.assertTrue(WATCHDOG.exists(), "the watchdog state machine is missing")
        src = WATCHDOG.read_text(encoding="utf-8-sig")
        self.assertRegex(src, r"public bool Tick\(bool tunnelOk, bool trafficFlowing\)",
                         "the watchdog must take both the probe result and whether "
                         "traffic is flowing: a saturated link fails the probe")

    def test_flowing_traffic_outranks_a_failed_probe(self):
        # The bug: a download saturated the link, the probe timed out, and the
        # config was reloaded in the middle of the download. Bytes arriving are
        # proof the tunnel works, so a failed probe while traffic flows is not a
        # stall. Without this the user loses the download every time the link is busy.
        src = WATCHDOG.read_text(encoding="utf-8-sig")
        m = re.search(r"public bool Tick\(bool tunnelOk, bool trafficFlowing\)\s*\{(.{0,400})", src, re.S)
        self.assertIsNotNone(m, "Tick is missing")
        self.assertRegex(
            m.group(1),
            r"if\s*\(tunnelOk\s*\|\|\s*trafficFlowing\)",
            "a sample with traffic flowing must be treated as a healthy tunnel: the "
            "probe only times out because the link is saturated")

    def test_the_service_checks_traffic_before_probing(self):
        # Checking traffic first is what avoids the false alarm: the probe is the
        # thing that misreads a busy link, so it must not even run while data flows.
        src = SERVICE.read_text(encoding="utf-8-sig")
        m = re.search(r"private async Task TickAsync\(CancellationToken token\)(.{0,1200})", src, re.S)
        self.assertIsNotNone(m, "TickAsync is missing")
        block = m.group(1)
        traffic_at = block.find("BytesMovedSinceLastTick()")
        probe_at = block.find("ProbeAsync(")
        self.assertGreaterEqual(traffic_at, 0, "the service must sample real traffic")
        self.assertGreaterEqual(probe_at, 0, "the service must still probe")
        self.assertLess(
            traffic_at, probe_at,
            "the traffic check must come BEFORE the probe: when bytes are moving "
            "there is nothing to probe and nothing to fix")
        # And it must actually report the traffic, or the whole check is a no-op.
        self.assertRegex(
            block,
            r"BytesMovedSinceLastTick\(\)\s*>=\s*MinBytesPerTick\)\s*\{\s*"
            r"_watchdog\.Tick\(tunnelOk:\s*true,\s*trafficFlowing:\s*true\)",
            "the traffic branch must tell the watchdog that traffic is flowing: "
            "passing trafficFlowing: false here makes the check do nothing")

    def test_the_traffic_signal_counts_received_bytes_only(self):
        # Bytes sent into a dead tunnel keep leaving the machine until the socket
        # buffer fills, so counting sends would call a wedged tunnel busy.
        src = SERVICE.read_text(encoding="utf-8-sig")
        m = re.search(r"private long BytesMovedSinceLastTick\(\)(.{0,700})", src, re.S)
        self.assertIsNotNone(m, "the traffic sampler is missing")
        block = m.group(1)
        self.assertIn("BytesReceived", block, "the sampler must read received bytes")
        self.assertNotIn(
            "BytesSent", block,
            "the sampler must not count sent bytes: they keep flowing into a dead "
            "tunnel, so a wedged connection would look busy")

    def test_the_probe_timeout_is_longer_than_the_latency_timeout(self):
        # A liveness check is not a speed test. On a saturated link a small request
        # takes longer than the 5 s latency budget, and scoring that as dead is the
        # false alarm itself.
        src = SERVICE.read_text(encoding="utf-8-sig")
        self.assertRegex(
            src, r"ProbeTimeout\s*=\s*TimeSpan\.FromSeconds\(\d+\)",
            "the probe must have its own timeout")
        m = re.search(r"ProbeTimeout\s*=\s*TimeSpan\.FromSeconds\((\d+)\)", src)
        self.assertGreaterEqual(
            int(m.group(1)), 10,
            "the probe timeout must be well above the 5 s latency budget, or a slow "
            "link is scored as a dead tunnel")
        self.assertIn(
            "GetRealPingQuality(webProxy, 1, token, ProbeTimeout)", src,
            "the probe must actually pass its longer timeout to the request")

    def test_an_idle_or_never_working_tunnel_never_reloads(self):
        # The app is idle most of the time and the server is sometimes simply
        # down. Reloading the config in those cases would restart the core over
        # and over for nothing.
        src = WATCHDOG.read_text(encoding="utf-8-sig")
        self.assertRegex(
            src, r"if\s*\(!_armed\)\s*\{[^}]*return false;",
            "the watchdog must stay disarmed until the tunnel answered once: "
            "a server that is down cannot be fixed by reloading the config")

    def test_the_reload_goes_through_the_reload_button_path(self):
        # The Reload button is MainWindowViewModel.Reload(), reached through
        # StatusBarViewModel.ReloadRequested. A separate restart would not
        # update the system proxy or the icon, and would not be the same fix.
        src = SERVICE.read_text(encoding="utf-8-sig")
        self.assertIn(
            "StatusBarViewModel.Instance.ReloadRequested.Publish()",
            src,
            "the watchdog must request the reload through the Reload button's "
            "own path (StatusBarViewModel.ReloadRequested)")

    def test_the_reload_is_not_repeated_for_the_same_stall(self):
        src = WATCHDOG.read_text(encoding="utf-8-sig")
        self.assertRegex(
            src, r"if\s*\(_reloadRequested\)\s*\{[^}]*return false;",
            "one stall must produce one reload: the core is restarting and will "
            "not answer until it is back")

    def test_a_success_clears_the_failure_streak(self):
        # A tunnel that answers again is healthy: the streak is over. Without this
        # the streak only ever grows, and the watchdog fires on the first blip after
        # a single hiccup.
        src = WATCHDOG.read_text(encoding="utf-8-sig")
        m = re.search(r"public bool Tick\(bool tunnelOk, bool trafficFlowing\)\s*\{(.{0,600})", src, re.S)
        self.assertIsNotNone(m, "Tick is missing")
        block = m.group(1)
        ok_branch = re.search(r"if\s*\(tunnelOk\s*\|\|\s*trafficFlowing\)\s*\{(.{0,300}?)\}", block, re.S)
        self.assertIsNotNone(ok_branch, "the success branch is missing")
        self.assertRegex(
            ok_branch.group(1), r"_consecutiveFailures\s*=\s*0",
            "a successful probe must clear the failure streak")
        self.assertRegex(
            ok_branch.group(1), r"_reloadRequested\s*=\s*false",
            "a successful probe must re-arm the watchdog: the core is back, so a "
            "later stall has to be caught again")

    def test_the_watchdog_waits_for_the_core_before_probing(self):
        # Probing a core that is not running is pointless, and reloading a
        # config the user deliberately turned off would be worse.
        src = SERVICE.read_text(encoding="utf-8-sig")
        self.assertIn(
            "IsRunningCore(ECoreType.Xray)",
            src,
            "the watchdog must check that a core is running before it probes")
        self.assertIn(
            "IsRunningCore(ECoreType.sing_box)",
            src,
            "the watchdog must check that a core is running before it probes")

    def test_the_watchdog_stops_when_the_app_exits(self):
        src = SERVICE.read_text(encoding="utf-8-sig")
        self.assertIn(
            "AppEvents.AppExitRequested",
            src,
            "the watchdog must stop when the app exits, so a probe cannot run "
            "while everything else is shutting down")

    def test_the_watchdog_is_started_with_the_app(self):
        src = MAIN_VM.read_text(encoding="utf-8-sig")
        self.assertIn(
            "_trafficWatchdog = new TrafficWatchdogService(",
            src,
            "the watchdog must be started when the app starts")
        self.assertRegex(src, r"_trafficWatchdog\.Start\(\)",
                         "the watchdog must actually be started")

    def test_a_failed_probe_does_not_throw_the_loop_over(self):
        # A probe failure is data, not a crash: the loop has to keep running.
        src = SERVICE.read_text(encoding="utf-8-sig")
        m = re.search(r"private async Task RunAsync\(CancellationToken token\)(.{0,900})", src, re.S)
        self.assertIsNotNone(m, "the probe loop is missing")
        block = m.group(1)
        self.assertRegex(
            block, r"catch\s*\(Exception ex\)\s*\{[^}]*Logging\.SaveLog",
            "a failing probe must be logged, not allowed to kill the watchdog loop")
        self.assertNotRegex(
            block, r"catch\s*\(Exception ex\)\s*\{\s*throw;",
            "the loop must swallow a failing probe: throwing kills the watchdog for "
            "the rest of the session")


if __name__ == "__main__":
    unittest.main()
