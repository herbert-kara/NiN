"""Outline dynamic keys (ssconf://) must stay resolvable on every path that matter.

The ssconf link carries no cipher data: it is an https rendezvous that answers
with an ss:// link or an outline-sdk YAML document. Two ways to get that wrong
without any test noticing: importing it but never resolving it (nothing to
connect to), and importing it fine but storing the ssconf URL on the
subscription, which the download service then tries to fetch as https forever.
Each test below pins one of the call sites or the parse contract.
"""
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
FMT = REPO / "v2rayN/ServiceLib/Handler/Fmt/SsConfFmt.cs"
IMPORTER = REPO / "v2rayN/ServiceLib/Handler/ConfigHandler.cs"
GLOBAL = REPO / "v2rayN/ServiceLib/Global.cs"


def read(path):
    return path.read_text(encoding="utf-8-sig")


class SsConfImportTests(unittest.TestCase):
    def setUp(self):
        self.fmt = read(FMT)
        self.importer = read(IMPORTER)
        self.glob = read(GLOBAL)

    def test_scheme_is_declared_once(self):
        self.assertIn('SsConfProtocol = "ssconf://"', self.glob)

    def test_scheme_is_not_a_share_scheme(self):
        """ssconf is a rendezvous, not something this app exports: exporting it
        would make GetShareUri emit a link nothing else can read."""
        shares = self.glob[self.glob.index("ProtocolShares = new()"):]
        shares = shares[:shares.index("};")]
        self.assertNotIn("SsConfProtocol", shares)

    def test_importer_resolves_a_pasted_key(self):
        self.assertIn("(profileItem, msg) = await SsConfFmt.ResolveAsync(str)", self.importer)

    def test_resolution_happens_after_the_plain_text_parsers(self):
        """Order matters: an ss:// share link must keep going through
        ShadowsocksFmt, and only an unmatched ssconf:// line gets fetched."""
        resolve = self.importer.index("FmtHandler.ResolveConfig(str, out var msg)")
        fallback = self.importer.index("SsConfFmt.IsSsConf(str)")
        self.assertLess(resolve, fallback)

    def test_resolution_happens_before_the_custom_import(self):
        fallback = self.importer.index("SsConfFmt.IsSsConf(str)")
        custom = self.importer.index("AddBatchServers4Custom(")
        self.assertLess(
            fallback, custom,
            "after the Custom import an ssconf link would be filed as an opaque file",
        )

    def test_subscription_never_keeps_the_ssconf_scheme(self):
        """Stored as ssconf:// the download service would keep trying to fetch
        it; stored as https:// every refresh re-resolves the key."""
        self.assertIn("subItem.Url = SsConfFmt.ToHttpUrl(subItem.Url)", self.importer)

    def test_add_by_url_imports_a_key_instead_of_storing_it(self):
        """A key is one profile. Stored as a subscription, every refresh would
        download a key document and import nothing."""
        marker = "public static async Task<int> AddSubItem(Config config, string url)"
        body = self.importer[self.importer.index(marker):self.importer.index(marker) + 900]
        self.assertIn("AddBatchServers(config, url, config.SubIndexId, false)", body)
        self.assertNotIn("ToHttpUrl(url)", body)

    def test_key_name_becomes_the_remark(self):
        """Outline keys have no host worth showing twice; the fragment is the
        name the user recognises."""
        self.assertIn("GetKeyName(ssconfUrl)", self.fmt)
        self.assertIn("item.Remarks = name", self.fmt)

    def test_resolver_sends_the_request_without_the_fragment(self):
        self.assertRegex(self.fmt, r"TryGetAsync\(ToHttpUrl\(")

    def test_resolver_reads_both_payload_shapes(self):
        self.assertIn("ShadowsocksFmt.Resolve(text, out msg)", self.fmt)
        self.assertIn("ResolveOutlineKey(text, out msg)", self.fmt)

    def test_unsupported_transport_is_reported_not_imported(self):
        """A websocket key must fail loudly: importing it as Shadowsocks would
        produce a profile that cannot connect."""
        self.assertIn("ResUI.NonvmessOrssProtocol", self.fmt)
        self.assertNotIn("EConfigType.Custom", self.fmt)

    def test_outline_prefix_camouflage_stays_unmodelled(self):
        """The prefix is optional camouflage the server tolerates on its own
        (verified against a live key), so reading it would be dead weight."""
        self.assertNotIn('"prefix"', self.fmt)

    def test_unit_cases_cover_the_parse_contract(self):
        tests = read(REPO / "v2rayN/ServiceLib.Tests/Fmt/SsConfFmtTests.cs")
        for case in ("ResolveOutlineKey_ShouldMapTheTcpTransport",
                     "ResolveOutlineKey_ShouldReportAnUnsupportedTransport",
                     "ResolveOutlineKey_ShouldRejectIncompleteOrBrokenKeys",
                     "ToHttpUrl_ShouldDropTheFragmentAndKeepTheQuery"):
            with self.subTest(case=case):
                self.assertIn(case, tests)


    def test_resolve_does_not_use_an_out_parameter(self):
        """async methods cannot carry out/ref parameters; the message rides in a tuple."""
        source = read(FMT)
        self.assertNotIn("ResolveAsync(string ssconfUrl, out", source)
        self.assertIn("Task<(ProfileItem? Item, string Msg)> ResolveAsync", source)


if __name__ == "__main__":
    unittest.main()
