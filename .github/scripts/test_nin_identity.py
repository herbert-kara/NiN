"""User-visible identity and decoded tray palette regression checks."""
import unittest
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
COLORS = [(51, 153, 204), (147, 51, 234), (220, 38, 38), (22, 128, 61)]

class NiNIdentityTests(unittest.TestCase):
    def test_display_brand_preserves_update_identity(self):
        self.assertTrue('AppName = "NiN"' in (ROOT / 'v2rayN/ServiceLib/Global.cs').read_text(encoding='utf-8-sig'))
        for project in ['v2rayN/v2rayN/v2rayN.csproj', 'v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj']:
            text = (ROOT / project).read_text(encoding='utf-8-sig')
            self.assertTrue('<Product>NiN</Product>' in text)
            self.assertTrue('<AssemblyName>NiN</AssemblyName>' in text)

    def test_persistent_identity_survives_display_rename(self):
        text = (ROOT / 'v2rayN/ServiceLib/Global.cs').read_text(encoding='utf-8-sig')
        self.assertIn('public const string AppId = "NiN";', text)
        self.assertIn('AutoRunName = "v2rayNAutoRun"', text)
        webdav = (ROOT / 'v2rayN/ServiceLib/Manager/WebDavManager.cs').read_text(encoding='utf-8-sig')
        self.assertEqual(webdav.count('Global.AppId + "_backup"'), 2)
        self.assertIn('_config.WebDavItem.DirName.TrimEx()', webdav)
        self.assertNotIn('Global.AppName', webdav)
        startup = (ROOT / 'v2rayN/ServiceLib/Handler/AutoStartupHandler.cs').read_text(encoding='utf-8-sig')
        for suffix in ['.desktop', '-LaunchAgent.plist', '-LaunchAgent</string>']:
            self.assertIn('{Global.AppId}' + suffix, startup)
        self.assertNotIn('Global.AppName', startup)
        self.assertIn('{Global.AutoRunName}_{Utils.GetMd5(Utils.StartupPath())}', startup)

    def test_localized_display_strings_use_nin(self):
        import xml.etree.ElementTree as ET
        resources = list((ROOT / 'v2rayN/ServiceLib/Resx').glob('ResUI*.resx'))
        resources += list((ROOT / 'v2rayN/AmazTool/Resx').glob('Resource*.resx'))
        self.assertGreater(len(resources), 10)
        for path in resources:
            for data in ET.parse(path).getroot().findall('data'):
                value = data.findtext('value') or ''
                with self.subTest(file=path.name, key=data.get('name')):
                    self.assertNotIn('NimN', value)
        update = (ROOT / 'v2rayN/ServiceLib/Services/UpdateService.cs').read_text(encoding='utf-8-sig')
        self.assertIn('"No complete NiN release available"', update)
        self.assertIn('NiNRelease.SelectTag', update)

    def test_tray_colors_visible_at_all_icon_sizes(self):
        for folder in ['v2rayN/v2rayN/Resources', 'v2rayN/v2rayN.Desktop/Assets']:
            for number, rgb in enumerate(COLORS, 1):
                with Image.open(ROOT / folder / f'NotifyIcon{number}.ico') as icon:
                    for size in sorted(icon.ico.sizes()):
                        with self.subTest(folder=folder, mode=number, size=size):
                            im = icon.ico.getimage(size).convert('RGBA')
                            # Small ICO frames are resampled; allow 8/255 antialiasing error.
                            hits = sum(im.getpixel((x, y))[3] > 240 and
                                       max(abs(im.getpixel((x, y))[i] - c) for i, c in enumerate(rgb)) <= 8
                                       for x in range(im.width) for y in range(im.height))
                            self.assertGreater(hits, size[0] * size[1] * 0.20)

if __name__ == '__main__':
    unittest.main()
