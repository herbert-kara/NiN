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

    def test_no_field_assignment_on_init_only_records(self):
        """TransportExtraItem/ProtocolExtraItem are records with init setters.

        Assigning `transport.Host = x` after construction is CS8850/CS8852 and
        will not compile; every field has to go in the object initializer.
        """
        import re as _re
        records = {}
        for name in ("TransportExtraItem", "ProtocolExtraItem"):
            path = REPO / "v2rayN/ServiceLib/Models/Entities" / f"{name}.cs"
            if not path.exists():
                continue
            src = path.read_text(encoding="utf-8-sig")
            fields = set(
                _re.findall(r"public\s+\w[\w<>?\.]*\s+(\w+)\s*\{\s*get;\s*init;", src)
            )
            if not fields:
                continue
            records[name] = fields

        # Local variable names in the converter, mapped to the record they hold.
        var_records = {"transport": "TransportExtraItem", "protocolExtra": "ProtocolExtraItem"}
        self.assertTrue(
            any(records.values()),
            "expected TransportExtraItem/ProtocolExtraItem to be records with init setters",
        )

        offenders = _re.findall(r"\n\s*(\w+)\.(\w+)\s*=[^=]", self.converter)
        for var, field in offenders:
            record = var_records.get(var)
            if record and field in records.get(record, set()):
                self.fail(
                    f"ProxyOutboundFmt.cs assigns {var}.{field}; {record} is a record "
                    "with init-only setters, so it must be set in the object initializer"
                )


if __name__ == "__main__":
    unittest.main()

class NiNProxyArrayImportTests(unittest.TestCase):
    """A subscription may return an array of complete Xray configs, not share links.

    Each element carries its own `outbounds`, so the resolver must walk the whole
    array. If it ever returns only the first entry, every real user of such a
    subscription silently gets a single node -- which is exactly the bug reported.
    """

    def test_resolver_walks_every_array_element(self):
        src = (REPO / "v2rayN/ServiceLib/Handler/Fmt/ProxyOutboundFmt.cs").read_text(
            encoding="utf-8-sig")
        self.assertIn("if (jsonNode is JsonArray array)", src)
        body = src.split("if (jsonNode is JsonArray array)", 1)[1].split("return result;", 1)[0]
        self.assertIn("foreach", body, "the array branch must iterate")
        self.assertIn("ResolveCommon(item, subRemarks)", body,
                      "each array element must be resolved, not just the first")

    def test_array_of_full_configs_is_tested(self):
        """Pin the multi-config shape so a regression is caught by CI, not a user."""
        tests = (REPO / "v2rayN/ServiceLib.Tests/Handler/ProxyOutboundFmtTests.cs").read_text(
            encoding="utf-8-sig")
        self.assertIn("ArrayOfFullConfigs_YieldsEveryNode", tests)
        # It must assert more than one entry survives, otherwise the test is vacuous.
        m = re.search(
            r"ArrayOfFullConfigs_YieldsEveryNode.*?list\.Count\.Should\(\)\.BeEqualTo\((\d+)\)",
            tests, re.S)
        self.assertIsNotNone(m, "the array test must assert an exact profile count")
        self.assertGreaterEqual(int(m.group(1)), 6,
                                "expected several nodes from a small array fixture")

    def test_plumbing_is_still_excluded(self):
        src = (REPO / "v2rayN/ServiceLib/Handler/Fmt/ProxyOutboundFmt.cs").read_text(
            encoding="utf-8-sig")
        for proto in ("freedom", "blackhole", "dns", "loopback"):
            self.assertIn(proto, src, f"{proto} must stay excluded as a plumbing outbound")

class NiNImportReachabilityTests(unittest.TestCase):
    """`AddBatchServers4Custom` is the only route into the structured resolver.

    It runs last and only when every earlier parser returned 0. Any earlier parser
    that returns >=1 stops the cascade, so a payload shape that partially parses
    anywhere earlier silently never reaches ProxyOutboundFmt. Pin the ordering.
    """

    CONFIG_HANDLER = REPO / "v2rayN/ServiceLib/Handler/ConfigHandler.cs"

    def test_custom_path_runs_after_the_line_parsers(self):
        src = self.CONFIG_HANDLER.read_text(encoding="utf-8-sig")
        i_common = src.index("AddBatchServersCommon(config, strData, subid, isSub)")
        i_custom = src.index("AddBatchServers4Custom(config, strData, subid, isSub)")
        self.assertLess(i_common, i_custom,
                        "the structured resolver must stay reachable from the cascade")

    def test_resolver_is_wired_before_the_custom_fallbacks(self):
        src = self.CONFIG_HANDLER.read_text(encoding="utf-8-sig")
        i_new = src.index("ProxyOutboundFmt.Resolve(strData, subRemarks)")
        i_v2ray = src.index("V2rayFmt.ResolveToCustom(strData, subRemarks)")
        self.assertLess(i_new, i_v2ray,
                        "ProxyOutboundFmt must win over the Custom fallbacks")

    def test_resolver_takes_precedence_over_the_generic_fallback(self):
        src = self.CONFIG_HANDLER.read_text(encoding="utf-8-sig")
        i_new = src.index("ProxyOutboundFmt.Resolve(strData, subRemarks)")
        i_fallback = src.index("ResolveToCustomOutbound(strData, subRemarks)")
        self.assertLess(i_new, i_fallback,
                        "ProxyOutboundFmt must run before ResolveToCustomOutbound too")

