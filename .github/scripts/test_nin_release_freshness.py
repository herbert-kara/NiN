"""A published release must not keep serving a build that is not its tag.

A tag that is moved to a new commit (a fix landing after the tag was first
pushed) used to leave the published release untouched: the workflow saw a
published release, printed "leaving assets immutable" and exited successfully.
The download then carried a binary that predates the fix, while the release
page named the new tag -- and the green run said nothing was wrong.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/nin-release.yml"


class NiNReleaseAssetFreshnessTests(unittest.TestCase):
    def setUp(self):
        self.src = WORKFLOW.read_text(encoding="utf-8-sig")

    def test_the_publish_step_records_the_built_commit(self):
        # Without a marker of which commit the assets came from, the workflow
        # cannot tell a stale release from a current one.
        self.assertIn(
            "build-commit:",
            self.src,
            "the release notes must record the commit the assets were built from")

    def test_a_moved_tag_replaces_the_published_assets(self):
        # The immutability check must compare the recorded commit with the
        # current one; a tag that moved has to replace the stale assets.
        self.assertRegex(
            self.src,
            r'if \[ "\$STATUS" = published \] && \[ "\$BUILT_COMMIT" = "\$CURRENT_COMMIT" \]',
            "the immutability check must compare the built commit with the "
            "current one: a moved tag has to replace the assets")
        self.assertRegex(
            self.src,
            r"Tag moved from \$BUILT_COMMIT to \$CURRENT_COMMIT",
            "a moved tag must be reported, not silently skipped")

    def test_an_unchanged_release_stays_immutable(self):
        # Re-running the workflow for a tag that was already published from the
        # same commit must not touch the assets: a user who downloaded them
        # keeps getting the same bytes.
        self.assertRegex(
            self.src,
            r'if \[ "\$STATUS" = published \] && \[ "\$BUILT_COMMIT" = "\$CURRENT_COMMIT" \]',
            "a release published from the same commit must be left alone")

    def test_the_upload_always_happens(self):
        # The assets are uploaded with --clobber, so a moved tag overwrites the
        # stale ones instead of failing on "already exists".
        self.assertIn(
            'gh release upload "$RELEASE_TAG" dist/*.zip dist/*.sha256 --clobber',
            self.src,
            "the upload must clobber: a rebuilt tag has to replace the old assets")


if __name__ == "__main__":
    unittest.main()
