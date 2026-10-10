"""Existing databases must gain the quality columns, or those writes vanish.

sqlite-net's CreateTable only builds a table that does not exist yet; it never adds
columns to one that does. A database created before the quality feature therefore
has no Jitter/PacketLoss/QualityScore, and every write fails silently.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "v2rayN/ServiceLib/Manager/AppManager.cs"


class NiNQualityMigrationTests(unittest.TestCase):
    def src(self):
        return APP.read_text(encoding="utf-8-sig")

    def test_migration_runs_at_startup(self):
        src = self.src()
        i_table = src.index("CreateTable<ProfileExItem>();")
        i_migrate = src.index("EnsureProfileExQualityColumns();", i_table)
        self.assertLess(i_migrate - i_table, 400,
                        "the column migration must run right after the table is created")
        self.assertNotIn("await EnsureProfileExQualityColumns", src,
                         "InitApp is synchronous; an await here does not compile")

    def test_every_quality_column_is_added_if_missing(self):
        src = self.src()
        for col in ("Jitter", "PacketLoss", "QualityScore"):
            self.assertRegex(
                src, rf"ALTER TABLE ProfileExItem ADD COLUMN {col} ",
                f"{col} must be added to an existing ProfileExItem table",
            )

    def test_migration_checks_before_altering(self):
        """ALTER on an existing column fails and would abort startup."""
        src = self.src()
        self.assertIn("if (!existing.Contains(column))", src)
        helper = (ROOT / "v2rayN/ServiceLib/Helper/SqliteHelper.cs").read_text(encoding="utf-8-sig")
        self.assertIn('PRAGMA table_info({table})', helper,
                      "the column list comes from PRAGMA table_info in SQLiteHelper")

    def test_migration_is_synchronous(self):
        """InitApp() is synchronous and called from three entry points."""
        src = self.src()
        self.assertRegex(src, r"private static void EnsureProfileExQualityColumns\(\)",
                         "the migration must be a synchronous void method")

    def test_row_type_exists_for_the_pragma_query(self):
        src = self.src()
        self.assertRegex(src, r"class TableInfoRow\b",
                         "PRAGMA table_info needs a row type to deserialise into")


if __name__ == "__main__":
    unittest.main()
