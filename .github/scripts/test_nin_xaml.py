"""Catch XAML attribute mistakes that only the compiler would otherwise find.

Both of these have cost a CI build in this repo:

- `ToolTip=` on a DataGridTextColumn. The column type has no such property;
  the tooltip has to live on the cell (CellStyle in WPF, CellTemplate in
  Avalonia). Reading the project as XML catches it without compiling.
- Attributes on a typed element, where the declared type does not carry them.
  Again only a compile would say so.
"""
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]

XAML = [
    "v2rayN/v2rayN/Views/ProfilesView.xaml",
    "v2rayN/v2rayN.Desktop/Views/ProfilesView.axaml",
]

W_NS = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
AV_NS = "{https://github.com/avaloniaui}"

# Columns are the only elements that hide their contents in a template; a tooltip
# on one of these has to go inside CellStyle/CellTemplate.
COLUMN_TAGS = {"DataGridTextColumn", "DataGridTemplateColumn", "MyDGTextColumn",
               "MyDGFlagColumn", "MyDGCountryColumn", "DataGridComboBoxColumn",
               "DataGridCheckBoxColumn"}


def local(tag):
    return tag.rsplit("}", 1)[-1]


def parse(rel):
    return ET.fromstring((REPO / rel).read_text(encoding="utf-8-sig")), rel


class XamlAttributeTests(unittest.TestCase):
    def test_all_views_parse(self):
        for rel in XAML:
            with self.subTest(view=rel):
                parse(rel)

    def test_no_tooltip_directly_on_a_column(self):
        """A column has no ToolTip; put it in the cell template instead."""
        for rel in XAML:
            root, _ = parse(rel)
            for elem in root.iter():
                if local(elem.tag) in COLUMN_TAGS and "ToolTip" in elem.attrib:
                    self.fail(
                        f"{rel}: {local(elem.tag)} has a direct ToolTip attribute; "
                        "use CellStyle (WPF) or CellTemplate (Avalonia)"
                    )

    def test_no_wpf_only_markup_in_avalonia(self):
        """ElementStyle/CellStyle resolve on DataGridTextColumn in WPF only."""
        wpf_only = {"ElementStyle", "CellStyle"}
        for rel in XAML:
            if not rel.endswith(".axaml"):
                continue
            root, _ = parse(rel)
            for elem in root.iter():
                offender = wpf_only & set(elem.attrib)
                self.assertEqual(
                    offender, set(),
                    f"{rel}: <{local(elem.tag)}> uses {offender}, which Avalonia's "
                    "DataGridTextColumn does not resolve"
                )

    def test_style_elements_declare_a_target_type(self):
        """A Style without TargetType cannot type its Setters (AVLN2200)."""
        for rel in XAML:
            root, _ = parse(rel)
            for parent in root.iter():
                for elem in parent:
                    if local(elem.tag) != "Style":
                        continue
                    with self.subTest(view=rel, parent=local(parent.tag)):
                        self.assertIn(
                            "TargetType",
                            elem.attrib,
                            f"{rel}: a <Style> under <{local(parent.tag)}> has no TargetType",
                        )

    def test_static_resx_references_resolve(self):
        """Every x:Static ResUI.X must be a real Designer property."""
        designer = (REPO / "v2rayN/ServiceLib/Resx/ResUI.Designer.cs").read_text(encoding="utf-8-sig")
        known = set(re.findall(r'public\s+static\s+string\s+(\w+)\s*\{', designer))
        for rel in XAML:
            text = (REPO / rel).read_text(encoding="utf-8-sig")
            for name in set(re.findall(r'ResUI\.(\w+)', text)):
                with self.subTest(view=rel, key=name):
                    self.assertIn(name, known)


if __name__ == "__main__":
    unittest.main()