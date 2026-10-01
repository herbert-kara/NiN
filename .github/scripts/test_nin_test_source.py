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


if __name__ == "__main__":
    unittest.main()
