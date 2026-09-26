"""Fail-closed behavior tests for nin_guard.py, run against an isolated temp root.

Copies only the files nin_guard inspects (required text files + the asset
manifest and every file it hashes) from the real repo into a pristine
snapshot, clones the snapshot per test, points nin_guard.ROOT at the clone,
and verifies that check() passes on an intact tree and fails closed when
NiN identity/branding/integration markers are lost. No real repo file is
ever modified.
"""
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import nin_guard  # noqa: E402

REPO = Path(nin_guard.ROOT)

ASSET_MANIFEST = '.github/nin-assets.json'
TEXT_FILES = [
    'v2rayN/ServiceLib/Global.cs',
    'v2rayN/ServiceLib/Services/UpdateService.cs',
    'v2rayN/ServiceLib/Common/NiNRelease.cs',
    'v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs',
    'v2rayN/ServiceLib/Models/Dto/ProfileItemModel.cs',
    'v2rayN/v2rayN/Views/ProfilesView.xaml',
    'v2rayN/v2rayN/Base/MyDGCountryColumn.cs',
    'v2rayN/ServiceLib/Services/ServerFlaggedService.cs',
    'v2rayN/ServiceLib/Services/ServerCountryService.cs',
    'v2rayN/v2rayN/Base/MyDGFlagColumn.cs',
    'v2rayN/v2rayN.Desktop/Views/ProfilesView.axaml',
    'v2rayN/v2rayN/v2rayN.csproj',
    'v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj',
    '.github/workflows/nin-release.yml',
]
NI_ICON = 'v2rayN/v2rayN.Desktop/Assets/NotifyIcon1.ico'
WPF_XAML = 'v2rayN/v2rayN/Views/ProfilesView.xaml'
WPF_NEEDLE = '<base:MyDGCountryColumn'
AUTO_RESOLVER_CS = 'v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs'
AUTO_RESOLVER_NEEDLE = 'ServerCountryService.Instance.ResolveAsync'
WORKFLOW = '.github/workflows/nin-release.yml'


def _copy(src: Path, dst: Path) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)


class NiNGuardFailClosedTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Pristine snapshot built once from the real repo (read-only source).
        cls.pristine = Path(tempfile.mkdtemp(prefix='nin_guard_pristine_'))
        _copy(REPO / ASSET_MANIFEST, cls.pristine / ASSET_MANIFEST)
        for rel in TEXT_FILES:
            _copy(REPO / rel, cls.pristine / rel)
        for rel in json.loads((REPO / ASSET_MANIFEST).read_text(encoding='utf-8')):
            _copy(REPO / rel, cls.pristine / rel)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.pristine, ignore_errors=True)

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix='nin_guard_root_'))
        shutil.copytree(self.pristine, self.root, dirs_exist_ok=True)
        self._old_root = nin_guard.ROOT
        nin_guard.ROOT = self.root  # guard now reads only the isolated clone

    def tearDown(self):
        nin_guard.ROOT = self._old_root
        shutil.rmtree(self.root, ignore_errors=True)

    # -- scenarios ---------------------------------------------------------

    def test_normal_intact_tree_passes(self):
        # Must not raise and must reach the success print.
        self.assertEqual(nin_guard.check(), None)

    def test_altered_notify_icon_fails(self):
        icon = self.root / NI_ICON
        data = bytearray(icon.read_bytes())
        data[len(data) // 2] ^= 0xFF  # flip one byte, same size
        icon.write_bytes(bytes(data))
        with self.assertRaises(RuntimeError) as ctx:
            nin_guard.check()
        self.assertIn('icon/flag changed', str(ctx.exception))
        self.assertIn('NotifyIcon1.ico', str(ctx.exception))

    def test_removed_wpf_country_column_fails(self):
        xaml = self.root / WPF_XAML
        text = xaml.read_text(encoding='utf-8-sig')
        self.assertIn(WPF_NEEDLE, text)  # pristine copy really contains it
        xaml.write_text(text.replace(WPF_NEEDLE, 'DeletedColumn'),
                        encoding='utf-8-sig')
        with self.assertRaises(RuntimeError) as ctx:
            nin_guard.check()
        self.assertIn('NiN customization lost', str(ctx.exception))
        self.assertIn(WPF_XAML, str(ctx.exception))
        self.assertIn(WPF_NEEDLE, str(ctx.exception))

    def test_removed_auto_resolver_fails(self):
        cs = self.root / AUTO_RESOLVER_CS
        text = cs.read_text(encoding='utf-8-sig')
        self.assertIn(AUTO_RESOLVER_NEEDLE, text)
        cs.write_text(text.replace(AUTO_RESOLVER_NEEDLE, '/* gone */'),
                      encoding='utf-8-sig')
        with self.assertRaises(RuntimeError) as ctx:
            nin_guard.check()
        self.assertIn('NiN customization lost', str(ctx.exception))
        self.assertIn(AUTO_RESOLVER_CS, str(ctx.exception))
        self.assertIn(AUTO_RESOLVER_NEEDLE, str(ctx.exception))

    def test_missing_required_file_fails_closed(self):
        # Fail closed even on IO errors, not just content mismatches.
        (self.root / WPF_XAML).unlink()
        with self.assertRaises(OSError):
            nin_guard.check()

    def test_zip_of_folder_instead_of_contents_fails(self):
        # Regression: archiving $PACKAGE itself nested every entry under
        # NiN-windows-64/, so the in-app updater wrote a new tree beside the
        # running build and the old UI kept launching.
        wf = self.root / WORKFLOW
        text = wf.read_text(encoding='utf-8-sig')
        self.assertIn('Push-Location $env:PACKAGE', text)
        wf.write_text(
            text.replace('Push-Location $env:PACKAGE', 'Push-Location $env:GITHUB_WORKSPACE')
                 .replace('is not at the zip root', 'was nested'),
            encoding='utf-8-sig')
        with self.assertRaises(RuntimeError) as ctx:
            nin_guard.check()
        self.assertIn('package CONTENTS', str(ctx.exception))


if __name__ == '__main__':
    unittest.main(verbosity=2)
