"""The proxy-outbound converter must stay wired into the subscription import path.

`ProxyOutboundFmt.Resolve` can compile and pass its own tests while nothing calls
it, which is exactly what happened to `V2rayFmt.ResolveFullToOutbound` upstream.
This asserts the call site exists and sits ahead of the Custom fallback, because
the whole point is that these payloads become testable profiles rather than
opaque files.
"""
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CONVERTER = REPO / "v2rayN/ServiceLib/Handler/Fmt/ProxyOutboundFmt.cs"
IMPORTER = REPO / "v2rayN/ServiceLib/Handler/ConfigHandler.cs"


def read(path):
    return path.read_text(encoding="utf-8-sig")


class ProxyOutboundImportTests(unittest.TestCase):
    def setUp(self):
        self.converter = read(CONVERTER)
        self.importer = read(IMPORTER)

    def test_converter_skips_the_clients_own_outbounds(self):
        """freedom/blackhole/dns belong to the provider's client, not to a node."""
        for proto in ("freedom", "blackhole", "dns"):
            with self.subTest(protocol=proto):
                self.assertRegex(self.converter, rf'"{proto}"')

    def test_converter_covers_the_protocols_in_use(self):
        for proto in ("vless", "vmess", "trojan", "shadowsocks"):
            with self.subTest(protocol=proto):
                self.assertRegex(self.converter, rf'\["{proto}"\]\s*=\s*EConfigType')

    def test_converter_carries_reality_public_key(self):
        """Without the key a REALITY profile fails validation and never connects."""
        self.assertIn("publicKey", self.converter)
        self.assertIn("PublicKey = publicKey", self.converter)

    def test_converter_never_builds_a_custom_profile(self):
        """The whole reason this converter exists: no opaque EConfigType.Custom."""
        self.assertNotIn("EConfigType.Custom", self.converter)

    def test_importer_calls_the_converter(self):
        self.assertIn("ProxyOutboundFmt.Resolve(strData", self.importer)

    def test_converter_runs_before_the_custom_fallback(self):
        """After the fallback it would never be reached: Custom matches first."""
        call = self.importer.index("ProxyOutboundFmt.Resolve(strData")
        custom = self.importer.index("V2rayFmt.ResolveToCustom(strData")
        self.assertLess(
            call, custom,
            "ProxyOutboundFmt.Resolve must be tried before the Custom import, "
            "otherwise these payloads become opaque files again",
        )

    def test_fallback_is_kept_for_payloads_the_converter_rejects(self):
        """A converter that claims everything would break normal subscriptions."""
        self.assertRegex(
            self.importer,
            r"ProxyOutboundFmt\.Resolve\([^;]*;\s*if\s*\(\s*lstProfiles\.Count\s*==\s*0\s*\)",
        )

    def test_local_variables_are_declared_before_first_use(self):
        """C# has no hoisting: touching a local before its `var` line is an error.

        This slipped through once in the converter (a Setter before the object was
        constructed) and a delimiter check cannot see it. Scope is per method, since
        a name declared in one method says nothing about another.
        """
        lines = self.converter.splitlines()

        # Split into method bodies by indentation: a line at 4 spaces starting with
        # "private"/"public"/"internal" starts a new method.
        starts = [
            i for i, l in enumerate(lines)
            if re.match(r"^    (private|public|internal|protected)\b.*\(", l)
        ]
        bounds = list(zip(starts, starts[1:] + [len(lines)]))

        for lo, hi in bounds:
            segment = lines[lo:hi]
            declared = {}
            for i, line in enumerate(segment):
                for m in re.finditer(r"\bvar\s+(\w+)\s*=", line):
                    declared.setdefault(m.group(1), i)

            for name, decl_at in declared.items():
                for i, line in enumerate(segment):
                    if i >= decl_at:
                        break
                    stripped = line.lstrip()
                    if stripped.startswith(("//", "*", "///")):
                        continue
                    if re.search(rf"\b{re.escape(name)}\b", line):
                        self.fail(
                            f"ProxyOutboundFmt.cs line {lo + i + 1} uses '{name}' but "
                            f"it is only declared on line {lo + decl_at + 1}"
                        )


if __name__ == "__main__":
    unittest.main()