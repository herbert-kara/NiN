"""No bundled or auto-discovered third-party VPN launchers.

A previous revision wired a "Firefox VPN" command straight into the profile
list, pointing at an executable in the developer's own work tree. That couples
the app to a machine-local tool that no user has: on any other machine the menu
entry is dead weight, and the hardcoded path leaks a local directory layout.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCES = [
    "v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs",
    "v2rayN/v2rayN/Views/ProfilesView.xaml",
    "v2rayN/v2rayN/Views/ProfilesView.xaml.cs",
    "v2rayN/ServiceLib/Resx/ResUI.resx",
    "v2rayN/ServiceLib/Resx/ResUI.Designer.cs",
]

# Machine-local paths that must never be baked into a shipped app.
LOCAL_PATH = re.compile(r"[A-Za-z]:\\(?!Program Files|Windows)|/home/[a-z]+/|/Users/[a-z]+/")


class NiNNoBundledVpnTests(unittest.TestCase):
    def test_no_vpn_launcher_remains(self):
        found = []
        for rel in SOURCES:
            path = ROOT / rel
            if not path.exists():
                continue
            src = path.read_text(encoding="utf-8-sig")
            for m in re.finditer(r"(?i)foxy|firefox\s*vpn", src):
                found.append(f"{rel}: {m.group(0)}")
        self.assertEqual(
            found, [],
            "the Firefox VPN launcher is gone; these leftovers still reference it: "
            f"{found}",
        )

    def test_no_absolute_machine_paths(self):
        found = []
        for rel in SOURCES:
            path = ROOT / rel
            if not path.exists():
                continue
            for i, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
                if LOCAL_PATH.search(line):
                    found.append(f"{rel}:{i}")
        self.assertEqual(
            found, [],
            f"absolute local paths baked into the app: {found}",
        )
