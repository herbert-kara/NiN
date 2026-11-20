"""A taken port must move the Aether block, not kill every Aether profile.

NiN derives four loopback ports from CoreBasicItem.AetherListenPort (L..L+3).
A VPN client holding L+1 made the core exit with "os error 10048" before it ever
scanned, and the app only reported "the tunnel did not come up" - "Aether ها
کار نمیکنن". The settings load now moves the whole block to the nearest free one,
once, so every reader of the setting dials the same ports. This guard keeps a
future merge from quietly going back to giving up on a taken port.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PORTS = ROOT / "v2rayN/ServiceLib/Aether/AetherPorts.cs"
LOAD = ROOT / "v2rayN/ServiceLib/Handler/ConfigHandler.cs"
CORE_LOAD = ROOT / "v2rayN/ServiceLib/Aether/AetherCoreLoad.cs"


class NiNAetherPortMoveTests(unittest.TestCase):
    def test_the_settings_load_moves_a_taken_block(self):
        src = LOAD.read_text(encoding="utf-8-sig")
        m = re.search(r"public static Config\? LoadConfig\(\).*?\n    \}", src, re.S)
        self.assertIsNotNone(m, "LoadConfig is missing")
        body = m.group(0)
        self.assertIn(
            "AetherPorts.FreeListenPort(",
            body,
            "a taken port of the block must move it, not stop every Aether core")
        self.assertIn(
            "config.CoreBasicItem.AetherListenPort = freeAetherPort",
            body,
            "the moved port must be stored in the setting: the core manager, the "
            "exit service and the Xray outbounds all read it, and a port only one "
            "of them knows about dials a listener nobody opened")

    def test_the_move_comes_after_the_mixed_port_is_known(self):
        src = LOAD.read_text(encoding="utf-8-sig")
        inbound = src.find("config.Inbound.Add(inItem)")
        move = src.find("AetherPorts.FreeListenPort(")
        self.assertGreater(inbound, -1, "the default inbound is missing")
        self.assertGreater(
            move, inbound,
            "the app's own ports (mixed..mixed+6) must be known before a block is "
            "picked, or the move can land on the local proxy")
        self.assertIn(
            "AetherPorts.LocalPortsOf(",
            src[move:move + 400],
            "the candidate block must stay clear of the app's own local ports")

    def test_the_probe_is_the_cores_own_tcp_and_udp_one(self):
        src = LOAD.read_text(encoding="utf-8-sig")
        move = src.find("AetherPorts.FreeListenPort(")
        self.assertIn(
            "AetherCoreLoad.CanListen",
            src[move:move + 400],
            "the secondary-socks inbound is UDP as well; a TCP-only probe calls a "
            "taken block free and the core still dies on it")
        self.assertIn(
            "public static bool CanListen(int port, bool udp)",
            CORE_LOAD.read_text(encoding="utf-8-sig"),
            "the one probe of a port must stay the core load's own")

    def test_the_search_is_bounded_and_keeps_the_setting_when_nothing_is_free(self):
        src = PORTS.read_text(encoding="utf-8-sig")
        m = re.search(r"public static int FreeListenPort\(.*?\n    \}", src, re.S)
        self.assertIsNotNone(m, "FreeListenPort is missing")
        body = m.group(0)
        self.assertRegex(
            body, r"step <= FreeBlockSearch",
            "the search must stop somewhere: walking the port range at every start "
            "is a stall, not a fix")
        self.assertRegex(
            body, r"candidate > LastListenPort",
            "a block needs three ports after it; past the last such port there is none")
        self.assertRegex(
            body, r"return preferred;\s*\}\s*$",
            "with no free block the setting is kept, so the banner still names the "
            "port that is taken instead of a number nobody asked for")
        self.assertIn(
            "canListen(port, true)",
            src,
            "every port of the block is probed over UDP as well (the probe's second "
            "argument); a delegate invocation cannot name its arguments, so the "
            "compiler - not this guard - is what catches 'udp: true' here")

    def test_the_banner_for_a_taken_port_is_still_there(self):
        src = CORE_LOAD.read_text(encoding="utf-8-sig")
        self.assertIn(
            "MsgAetherListenPortInUse",
            src,
            "when no block is free the user must still be told which port is taken")


if __name__ == "__main__":
    unittest.main()
