"""Stdlib-only unittests for .github/scripts/nin_sync.py (side-effect safety).

Every subprocess call is faked: no network, no real git commands, no remote
changes. Focus is on what the sync must and must never do to the repo:
publish only on a clean, guarded merge; never push after conflicts, guard
failures, protected-automation changes, or invalid upstream input.
"""
import contextlib
import importlib.util
import io
import json
import os
import re
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parent


def _load_module():
    spec = importlib.util.spec_from_file_location(
        "nin_sync_under_test", HERE / "nin_sync.py"
    )
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


nin_sync = _load_module()

OLD_TAG = "7.25.1-P26"
NEW_TAG = "7.26.0-P27"
OLD_SHA = "b1c967134c9223a3d7c3d3ee7651cec85c4f3339"
NEW_SHA = "deadbeef" * 5
BUILD_PROPS = (
    "<Project>\n  <PropertyGroup>\n    <Version>7.25.1</Version>\n"
    "  </PropertyGroup>\n</Project>\n"
)


class FakeRunner:
    """Stands in for nin_sync.run: records calls, fakes gh/git outcomes."""

    def __init__(self, upstream_tag=NEW_TAG, merge_fails=False, guard_fails=False,
                 ancestor_fails=False, protected_changes=(), existing_releases=()):
        self.calls = []
        self.pushes = []
        self.tags = []
        self.upstream_tag = upstream_tag
        self.merge_fails = merge_fails
        self.guard_fails = guard_fails
        self.ancestor_fails = ancestor_fails
        self.protected_changes = tuple(protected_changes)
        self.existing_releases = tuple(existing_releases)

    def __call__(self, *args):
        args = tuple(str(a) for a in args)
        self.calls.append(args)
        joined = " ".join(args)
        if "releases/latest" in joined:
            return json.dumps({"tag_name": self.upstream_tag})
        if "releases?per_page" in joined:
            # Paginated --slurp shape: a list of pages, each a list of releases.
            return json.dumps([[{"tag_name": t} for t in self.existing_releases]])
        if "rev-parse" in args:
            return NEW_SHA if "FETCH_HEAD" in joined else OLD_SHA
        if args[:2] == ("git", "merge-base"):
            if self.ancestor_fails:
                raise subprocess.CalledProcessError(1, args)
            return ""
        if args[:2] == ("git", "merge"):
            if self.merge_fails:
                raise subprocess.CalledProcessError(1, args)
            return ""
        if args[:2] == ("git", "diff"):
            if args[-1] in self.protected_changes:
                return args[-1]
            return ""
        if len(args) > 1 and args[1].endswith("nin_guard.py"):
            if self.guard_fails:
                raise subprocess.CalledProcessError(1, args)
            return "guards passed"
        if args[:2] == ("git", "tag"):
            self.tags.append(args[2])
            return ""
        if args[:2] == ("git", "push"):
            self.pushes.append(args)
            return ""
        return ""


class SyncTestCase(unittest.TestCase):
    maxDiff = None

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        (self.root / ".github" / "scripts").mkdir(parents=True)
        (self.root / "v2rayN").mkdir()
        self.state_path = self.root / ".github" / "nin-upstream.json"
        self.write_state(OLD_TAG, OLD_SHA)
        (self.root / "v2rayN" / "Directory.Build.props").write_text(
            BUILD_PROPS, encoding="utf-8")
        (self.root / ".github" / "scripts" / "nin_guard.py").write_text(
            "print('ok')\n", encoding="utf-8")
        self.github_output = self.root / "github_output.txt"
        self.github_output.write_text("", encoding="utf-8")
        old_cwd = os.getcwd()
        self.addCleanup(os.chdir, old_cwd)
        os.chdir(self.root)
        self._env = mock.patch.dict(os.environ, {
            "GITHUB_REPOSITORY": "herbert-kara/NiN",
            "GITHUB_OUTPUT": str(self.github_output),
        })
        self._env.start()
        self.addCleanup(self._env.stop)

    # -- helpers -----------------------------------------------------------
    def write_state(self, tag, sha):
        self.state_path.write_text(
            json.dumps({"tag": tag, "sha": sha}, indent=2) + "\n",
            encoding="utf-8")

    def read_state(self):
        return json.loads(self.state_path.read_text(encoding="utf-8"))

    def run_main(self, runner):
        stdout = io.StringIO()
        with mock.patch.object(nin_sync, "run", runner), \
                contextlib.redirect_stdout(stdout):
            nin_sync.main()
        return stdout.getvalue()

    def git_calls(self, runner):
        return [c for c in runner.calls if c[0] == "git"]

    def assert_no_publish(self, runner):
        self.assertEqual(runner.pushes, [], "pushed despite failure")
        self.assertEqual(runner.tags, [], "tagged despite failure")
        self.assertEqual(self.github_output.read_text(encoding="utf-8"), "",
                         "GITHUB_OUTPUT written despite failure")
        commits = [c for c in runner.calls if c[:2] == ("git", "commit")]
        self.assertEqual(commits, [], "committed despite failure")

    def assert_state_untouched(self):
        self.assertEqual(self.read_state(), {"tag": OLD_TAG, "sha": OLD_SHA})

    # -- 1. no-op -----------------------------------------------------------
    def test_noop_when_upstream_tag_already_integrated(self):
        runner = FakeRunner(upstream_tag=OLD_TAG)
        out = self.run_main(runner)
        self.assertIn("Already integrated 7.25.1-P26", out)
        self.assertEqual(self.git_calls(runner), [])
        self.assert_state_untouched()
        self.assert_no_publish(runner)

    # -- 2. invalid tag ------------------------------------------------------
    def test_invalid_upstream_tag_aborts_before_any_git_call(self):
        for bad in ("v7.26.0", "7.26", "7.26.0-P", "latest", "7.26.0-PXX"):
            with self.subTest(tag=bad):
                runner = FakeRunner(upstream_tag=bad)
                with self.assertRaises(ValueError):
                    self.run_main(runner)
                self.assertEqual(self.git_calls(runner), [])
                self.assert_no_publish(runner)
                self.assert_state_untouched()

    def test_validate_tag_accepts_release_shape(self):
        self.assertEqual(nin_sync.validate_tag("7.25.1-P26"), "7.25.1-P26")
        self.assertEqual(nin_sync.validate_tag("0.0.1-P0"), "0.0.1-P0")

    # -- 3. candidate tag semantic numeric shape ------------------------------
    def test_candidate_tag_uses_plain_integer_revision(self):
        # A timestamp used to be embedded here, so SemanticVersion read e.g.
        # 1789904268 as the revision and ranked it above every real release —
        # the in-app updater then kept reinstalling that stale tree and the flag
        # columns and refresh button disappeared again. Revisions are counters now.
        tag = nin_sync.candidate_tag("7.25.1", NEW_SHA, ["v7.25.1-nin.9", "v7.25.1-nin.10"])
        self.assertEqual(tag, "v7.25.1-nin.11")
        self.assertIsNone(re.fullmatch(r"v7\.25\.1-nin\.(\d+)\.([0-9a-f]{8})", tag))
        # Legacy timestamp tags must not push the counter forward.
        self.assertEqual(
            nin_sync.candidate_tag("7.25.1", NEW_SHA, ["v7.25.1-nin.1789904268.ed090c08"]),
            "v7.25.1-nin.1")
        # Base version change restarts the counter.
        self.assertEqual(
            nin_sync.candidate_tag("7.26.0", NEW_SHA, ["v7.25.1-nin.10"]), "v7.26.0-nin.1")
        # Deterministic for a given set of previous tags.
        self.assertEqual(tag, nin_sync.candidate_tag("7.25.1", NEW_SHA, ["v7.25.1-nin.9", "v7.25.1-nin.10"]))

    def test_candidate_tag_rejects_non_semver_base(self):
        for bad in ("v7.25.1", "7.25", "7.25.1-P26", ""):
            with self.subTest(base=bad):
                with self.assertRaises(ValueError):
                    nin_sync.candidate_tag(bad, NEW_SHA, [])

    # -- 4. successful merge publishes exactly one tag -------------------------
    def test_successful_merge_writes_state_tags_pushes_and_outputs(self):
        runner = FakeRunner(upstream_tag=NEW_TAG)
        out = self.run_main(runner)
        self.assertEqual(len(runner.tags), 1)
        release_tag = runner.tags[0]
        # state rewritten to the integrated upstream release, with resume candidate
        state = self.read_state()
        self.assertEqual(state["tag"], NEW_TAG)
        self.assertEqual(state["sha"], NEW_SHA)
        self.assertEqual(state["candidate"], release_tag)
        # tag shape: base from Directory.Build.props + plain integer revision.
        # No timestamp/sha suffix — that shape outranked every real release in
        # SemanticVersion and made the updater reinstall a stale tree.
        self.assertRegex(release_tag, r"^v7\.25\.1-nin\.\d{1,6}$")
        # pushed by exact ref, nothing else
        self.assertEqual(
            runner.pushes, [("git", "push", "origin", f"refs/tags/{release_tag}")])
        # GITHUB_OUTPUT contract for the caller's workflow
        self.assertIn(f"tag={release_tag}\n",
                      self.github_output.read_text(encoding="utf-8"))
        self.assertIn("Prepared", out)
        # merge must be a real merge candidate, never a discard strategy
        merge = [c for c in runner.calls if c[:2] == ("git", "merge")][0]
        self.assertIn("--no-commit", merge)
        self.assertIn("--no-ff", merge)
        self.assertIn(NEW_SHA, merge)
        self.assertFalse(any(str(a).startswith("-X") for a in merge),
                         "conflict-strategy discard option used")
        commit = [c for c in runner.calls if c[:2] == ("git", "commit")][0]
        self.assertIn(NEW_TAG, " ".join(commit))

    # -- 5. conflict -> no push ------------------------------------------------
    def test_merge_conflict_aborts_without_push_or_commit(self):
        runner = FakeRunner(merge_fails=True)
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_main(runner)
        self.assert_no_publish(runner)
        self.assert_state_untouched()

    # -- 6. guard failure -> no push -------------------------------------------
    def test_guard_failure_pushes_nothing(self):
        runner = FakeRunner(guard_fails=True)
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_main(runner)
        self.assert_no_publish(runner)
        self.assert_state_untouched()

    # -- extra side-effect safety ----------------------------------------------
    def test_upstream_touching_protected_automation_pushes_nothing(self):
        runner = FakeRunner(protected_changes=(".github/scripts",))
        with self.assertRaises(RuntimeError):
            self.run_main(runner)
        self.assert_no_publish(runner)
        self.assert_state_untouched()

    def test_non_nin_repository_aborts_before_any_call(self):
        runner = FakeRunner()
        stdout = io.StringIO()
        with mock.patch.dict(os.environ, {"GITHUB_REPOSITORY": "Nim4a/Other"}), \
                mock.patch.object(nin_sync, "run", runner), \
                contextlib.redirect_stdout(stdout):
            with self.assertRaises(RuntimeError):
                nin_sync.main()
        self.assertEqual(runner.calls, [])
        self.assert_no_publish(runner)
        self.assert_state_untouched()


if __name__ == "__main__":
    unittest.main(verbosity=2)
