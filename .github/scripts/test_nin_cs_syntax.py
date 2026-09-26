"""Tests for the C# pre-flight checker. It must catch the exact break CI found."""
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import nin_cs_syntax

REPO = Path(__file__).resolve().parents[2]
TARGET = "v2rayN/ServiceLib/Common/NiNRelease.cs"


class CsSyntaxCheckTest(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="nin_cs_"))
        for rel in nin_cs_syntax.WATCHED:
            src = REPO / rel
            dst = self.root / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            if src.exists():
                shutil.copy2(src, dst)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def test_intact_repo_passes(self):
        self.assertIn("passed", nin_cs_syntax.check(self.root))

    def test_missing_watched_file_fails_closed(self):
        (self.root / TARGET).unlink()
        with self.assertRaises(RuntimeError) as ctx:
            nin_cs_syntax.check(self.root)
        self.assertIn("MISSING", str(ctx.exception))

    def test_stray_paren_before_ampersand_is_caught(self):
        # The real break: a ')' closed the lambda early and '&&' followed.
        p = self.root / TARGET
        text = p.read_text(encoding="utf-8-sig")
        broken = text.replace(
            "&& r.Assets?.Any(a => a.Name == \"NiN-windows-64.zip\") == true\n"
            "                 && r.Assets?.Any(a => a.Name == \"NiN-windows-64-desktop.zip\") == true)",
            "&& ReleaseTag.IsMatch(r.TagName))\n"
            "            && r.Assets?.Any(a => a.Name == \"NiN-windows-64.zip\") == true\n"
            "            && r.Assets?.Any(a => a.Name == \"NiN-windows-64-desktop.zip\") == true)",
            1)
        self.assertNotEqual(text, broken, "fixture rewrite did not apply")
        p.write_text(broken, encoding="utf-8-sig")
        with self.assertRaises(RuntimeError) as ctx:
            nin_cs_syntax.check(self.root)
        # Either symptom is a valid catch: a mismatched closer, or a leftover brace.
        self.assertIn(TARGET, str(ctx.exception))
        self.assertRegex(str(ctx.exception), r"closes '\{'|unexpected '\}'|unclosed")

    def test_unbalanced_brace_is_caught(self):
        p = self.root / TARGET
        text = p.read_text(encoding="utf-8-sig")
        p.write_text(text.replace("    public static string? SelectTag", "    public static string? SelectTag {", 1),
                     encoding="utf-8-sig")
        with self.assertRaises(RuntimeError):
            nin_cs_syntax.check(self.root)

    def test_char_literals_are_not_treated_as_delimiters(self):
        # ConfigHandler.cs really contains: if (trimmed[0] is '{' or '[')
        p = self.root / "v2rayN/ServiceLib/Handler/ConfigHandler.cs"
        text = p.read_text(encoding="utf-8-sig")
        p.write_text(text + "\n// probe: var c = '{'; var d = '['; var e = '\\'';\n",
                     encoding="utf-8-sig")
        self.assertIn("passed", nin_cs_syntax.check(self.root))

    def test_strings_comments_and_verbatim_strings_are_ignored(self):
        p = self.root / TARGET
        p.write_text(
            "namespace X; // trailing ( unbalanced\n"
            "/* block ( with ) braces */\n"
            "class C { string s = \"a )( b {\"; string v = @\"c )( d {\"; "
            "char q = '\\\"'; }\n",
            encoding="utf-8-sig")
        self.assertIn("passed", nin_cs_syntax.check(self.root))


if __name__ == "__main__":
    unittest.main(verbosity=2)
