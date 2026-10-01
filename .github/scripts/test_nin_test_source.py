"""Catch TUnit assertion mistakes that only a C# compile would report.

Two have each cost a CI build in this repo:

- `Should().Be(...)`. TUnit has `BeEqualTo(...)`; `Be` does not resolve.
- `await` inside parentheses around a plain value: `(await Foo().Count).Should()`
  awaits the int `Count`, not an assertion, so CS1061 ('int' does not contain a
  definition for 'GetAwaiter'). Plain `await list.Count.Should()` is fine and is
  used throughout upstream.
"""
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
TESTS = REPO / "v2rayN/ServiceLib.Tests"

# Every Should() method actually used and compiling in this repo, captured from the
# test suite rather than guessed: awaiting and method names both matter.
KNOWN_ASSERTIONS = {
    "BeEqualTo", "BeTrue", "BeFalse", "BeNull", "NotBeNull", "BeEmpty", "NotBeEmpty",
    "NotBeNullOrEmpty", "BeEquivalentTo", "BeLessThanOrEqualTo", "Contain",
    "NotContain", "ContainKey", "NotContainKey", "OnlyContain", "HaveCount",
    "StartWith", "EndWith", "All", "BeGreaterThan", "BeLessThan",
}


def test_sources():
    return sorted(TESTS.rglob("*.cs"))


ROOT = Path(__file__).resolve().parents[2]
PROFILE_ITEM = ROOT / "v2rayN/ServiceLib/Models/Entities/ProfileItem.cs"
_ITEM_SRC = PROFILE_ITEM.read_text(encoding="utf-8-sig")
# Properties (a body brace follows the name) and methods (parentheses follow it).
PROFILE_ITEM_PROPS = set(
    re.findall(r"public\s+[\w\.<>\[\]\?,\s]+\s(\w+)\s*\{", _ITEM_SRC)
) | set(re.findall(r"public\s+[\w\.<>\[\]\?,\s]+\s(\w+)\s*\(", _ITEM_SRC))
# Members promoted from the embedded transport/protocol objects, used by tests.
PROFILE_ITEM_PROPS |= {
    "ConfigType", "Network", "Path", "Sni", "Port", "Id", "Password", "Address",
    "Remarks", "IndexId", "ShortId", "PublicKey", "Fingerprint", "AllowInsecure",
    "StreamSecurity", "HeaderType", "RequestHost", "ServiceName", "Mode",
    "Security", "AlterId", "Flow", "Method", "PreSocksPort", "Subid", "IsSub",
}


class ProfileItemMemberTests(unittest.TestCase):
    """A member that does not exist is CS1061, and CI is the only place to notice.

    Scoped to the NiN-authored tests: upstream test files legitimately name
    locals `item` for other types, so a bare `item.` scan would be all false
    positives and would get switched off instead of fixed.
    """

    # Only the converter tests build ProfileItem instances. PingQualityTests works
    # on the PingQuality struct, whose members are unrelated to ProfileItem.
    NIN_TESTS = [
        "v2rayN/ServiceLib.Tests/Handler/ProxyOutboundFmtTests.cs",
    ]

    def test_member_list_was_extracted(self):
        self.assertIn("Address", PROFILE_ITEM_PROPS,
                      "failed to read ProfileItem members; the guard would pass vacuously")
        self.assertGreater(len(PROFILE_ITEM_PROPS), 20)

    def test_nin_tests_only_use_real_profileitem_members(self):
        linq = {"Should", "ToString", "Count", "Select", "Distinct", "Where", "ToList",
                "Any", "All", "First", "Value", "Key", "OrderBy"}
        for rel in self.NIN_TESTS:
            path = ROOT / rel
            self.assertTrue(path.exists(), f"{rel} is missing")
            src = path.read_text(encoding="utf-8-sig")
            # Members are read off the local collections these tests build.
            pattern = re.compile(r"\b(?:item|profile|vless\[\d+\]|trojan\[\d+\]|steady)\.(\w+)")
            for i, line in enumerate(src.splitlines(), 1):
                if line.strip().startswith("//"):
                    continue
                for m in pattern.finditer(line):
                    name = m.group(1)
                    if name in linq:
                        continue
                    # Obsolete aliases: they exist but do not hold what the test
                    # means, so a passing assertion can be vacuous.
                    if name in {"Id", "Path", "Network"}:
                        self.fail(
                            f"{path.name}:{i} reads .{name}, an obsolete alias; read "
                            "the value through the accessor that owns it instead"
                        )
                    if name not in PROFILE_ITEM_PROPS:
                        self.fail(
                            f"{path.name}:{i} reads .{name} off a ProfileItem, which is "
                            f"not declared; CS1061 in CI. Known members: "
                            f"{sorted(PROFILE_ITEM_PROPS)}"
                        )


class TunitAssertionTests(unittest.TestCase):
    def test_files_exist(self):
        self.assertTrue(test_sources(), "no C# test sources found")

    def test_no_should_be_alias(self):
        for path in test_sources():
            src = path.read_text(encoding="utf-8-sig")
            for i, line in enumerate(src.splitlines(), 1):
                if re.search(r"\.Should\(\)\.Be\(", line):
                    self.fail(
                        f"{path.name}:{i} uses Should().Be; TUnit calls it BeEqualTo"
                    )

    def test_only_known_assertion_methods_are_used(self):
        """An unrecognised Should().X(...) will not resolve against TUnit."""
        for path in test_sources():
            src = path.read_text(encoding="utf-8-sig")
            for i, line in enumerate(src.splitlines(), 1):
                for m in re.finditer(r"\.Should\(\)\.(\w+)\(", line):
                    if m.group(1) not in KNOWN_ASSERTIONS:
                        self.fail(
                            f"{path.name}:{i} uses Should().{m.group(1)}(); known "
                            f"assertions are {sorted(KNOWN_ASSERTIONS)}"
                        )

    def test_every_assertion_is_awaited(self):
        """TUnit errors with TUnitAssertions0002 if an assertion is not awaited.

        The opposite of an earlier wrong guess: `await x.Should()` is required,
        and `await list.Count.Should()` is fine (upstream writes it that way).
        Assertions wrap across lines and may carry `.Because(...)`, so scan
        statements rather than physical lines.
        """
        for path in test_sources():
            src = path.read_text(encoding="utf-8-sig")
            for m in re.finditer(r"\.Should\(\)\s*\.\w+\(", src):
                start = src.rfind("\n", 0, m.start()) + 1
                line_no = src.count("\n", 0, start) + 1
                # Walk back over continuation lines to the start of the statement.
                head = src[:m.start()]
                nl = head.rfind(";")
                stmt = head[nl + 1:] + src[m.start():m.end()]
                stmt = stmt.strip()
                if stmt.lstrip().startswith("//"):
                    continue
                if "await" not in stmt:
                    self.fail(
                        f"{path.name}:{line_no} asserts without await; TUnit requires "
                        "every assertion to be awaited (TUnitAssertions0002)"
                    )

    def test_no_await_on_a_bare_value(self):
        """`(await Foo().Count).Should()` awaits the int, not the assertion (CS1061).

        The await must sit on the assertion itself, never inside parentheses around
        a plain value.
        """
        for path in test_sources():
            src = path.read_text(encoding="utf-8-sig")
            for i, line in enumerate(src.splitlines(), 1):
                if re.search(r"\(\s*await\s+[^;]*?\.(Count|Length)\s*\)", line):
                    self.fail(
                        f"{path.name}:{i} awaits a plain value inside parentheses; "
                        "await the assertion itself instead"
                    )

    def test_void_test_methods_are_not_awaiting(self):
        """A `void` test with `await` will not compile either."""
        for path in test_sources():
            src = path.read_text(encoding="utf-8-sig")
            blocks = re.split(r"\n    \[Test\]", src)
            for block in blocks[1:]:
                header = block.split("\n")[0:3]
                joined = "\n".join(header)
                if re.search(r"public\s+void\s+\w+", joined):
                    body = block.split("}", 1)[-1]
                    if "await " in body:
                        self.fail(
                            f"{path.name}: a void test contains await; make it async Task"
                        )


    def test_synchronous_calls_are_not_awaited(self):
        """`await SomeMethod(...)` on a plain sync return is CS1061.

        Only awaits on actual async calls (or on the assertion itself) are valid,
        so flag an await that sits directly on a call to a non-async method.
        """
        sync_calls = {
            "ProxyOutboundFmt.Resolve", "V2rayFmt.ResolveToCustom",
            "V2rayFmt.ResolveToCustomOutbound", "SingboxFmt.ResolveToCustom",
            "PingQuality.FromSamples",
        }
        for path in test_sources():
            src = path.read_text(encoding="utf-8-sig")
            for i, line in enumerate(src.splitlines(), 1):
                stripped = line.strip()
                if stripped.startswith("//"):
                    continue
                for call in sync_calls:
                    if f"await {call}(" in stripped and ".Should()" not in stripped:
                        self.fail(
                            f"{path.name}:{i} awaits {call}, which is synchronous; "
                            "drop the await (CS1061)"
                        )


if __name__ == "__main__":
    unittest.main()
