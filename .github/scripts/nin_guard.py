"""Fail closed if an upstream merge removes NiN's identity or country integration."""
from pathlib import Path
import hashlib
import json
import sys

ROOT = Path(__file__).resolve().parents[2]


def check():
    required = {
        'v2rayN/ServiceLib/Global.cs': ['AppName = "NiN"', '"herbert-kara/NiN"'],
        'v2rayN/ServiceLib/Models/Dto/PingQuality.cs': ['FromSamples', 'Jitter', 'Loss', 'Score'],
        'v2rayN/ServiceLib/Handler/ConnectionHandler.cs': ['GetRealPingQuality', 'RealPingSamples'],
        'v2rayN/ServiceLib/Services/SpeedtestService.cs': ['SetTestQuality'],
        'v2rayN/ServiceLib/Models/Entities/ProfileExItem.cs': ['Jitter', 'PacketLoss', 'QualityScore'],
        'v2rayN/ServiceLib/Services/UpdateService.cs': ['NiNRelease.SelectTag'],
        'v2rayN/ServiceLib/Common/NiNRelease.cs': ['SelectTag', r'-nin\.(?<rev>\d{1,6})'],
        'v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs': ['ServerCountryService.Instance.ResolveAsync', 'ServerFlaggedService.Instance.ResolveAsync', 'LookupServerFlagsAsync', 'AddFoxyVpnCmd', 'FoxyVPN.exe'],
        'v2rayN/ServiceLib/Models/Dto/ProfileItemModel.cs': ['ServerCountryCode', 'CountryCode'],
        'v2rayN/v2rayN/Views/ProfilesView.xaml': ['<base:MyDGCountryColumn', 'btnRefreshServerFlags', 'menuAddFoxyVpn'],
        'v2rayN/v2rayN/Base/MyDGCountryColumn.cs': ['CountryCode'],
        'v2rayN/v2rayN.Desktop/Views/ProfilesView.axaml': ['CountryFlagConverter', 'FlagStatusConverter'],
        'v2rayN/ServiceLib/Services/ServerFlaggedService.cs': ['proxycheck.io', 'EFlagStatus.Flagged', 'forceRefresh'],
        'v2rayN/ServiceLib/Services/ServerCountryService.cs': ['forceRefresh'],
        'v2rayN/v2rayN/Base/MyDGFlagColumn.cs': ['FlagStatus'],
        'v2rayN/v2rayN/v2rayN.csproj': ['<AssemblyName>NiN</AssemblyName>'],
        'v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj': ['<AssemblyName>NiN</AssemblyName>'],
    }
    for file, needles in required.items():
        text = (ROOT / file).read_text(encoding='utf-8-sig')
        for needle in needles:
            if needle not in text:
                raise RuntimeError(f'NiN customization lost: {file}: {needle}')
    for file, expected in json.loads((ROOT / '.github/nin-assets.json').read_text()).items():
        if hashlib.sha256((ROOT / file).read_bytes()).hexdigest() != expected:
            raise RuntimeError(f'NiN icon/flag changed: {file}')
    check_release_package_layout()
    check_cs_syntax()
    print('NiN identity, updater, flags, and automatic lookup guards passed')


def check_cs_syntax():
    """Run the C# delimiter pre-flight over the files NiN edits.

    Local compilation is not available on this machine and CI is the only build,
    so an unbalanced delimiter in a hand-edited .cs file used to reach the runner
    and fail the whole ServiceLib build. Cheap to check, so check it here.
    """
    sys.path.insert(0, str(ROOT / '.github/scripts'))
    import nin_cs_syntax
    nin_cs_syntax.check(ROOT)


def check_release_package_layout():
    """The in-app updater extracts the zip into the install directory.

    A leading ``NiN-windows-64/`` entry puts NiN.exe one level too deep, so the
    updater writes a new tree beside the running build and the old executable
    keeps launching without the new UI. The workflow now archives the package
    contents and asserts both exes sit at the zip root; guard that here so the
    packaging cannot silently regress.
    """
    text = (ROOT / '.github/workflows/nin-release.yml').read_text(encoding='utf-8-sig')
    if "Push-Location $env:PACKAGE" not in text:
        raise RuntimeError('release zip must archive the package CONTENTS, not the folder')
    if "is not at the zip root" not in text:
        raise RuntimeError('release workflow must assert NiN.exe/AmazTool.exe are at the zip root')
    if "nin_cs_syntax.py" not in text:
        raise RuntimeError('release workflow must run the C# syntax pre-flight before building')


if __name__ == '__main__':
    check()
