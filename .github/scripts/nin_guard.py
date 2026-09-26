"""Fail closed if an upstream merge removes NiN's identity or country integration."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]


def check():
    required = {
        'v2rayN/ServiceLib/Global.cs': ['AppName = "NiN"', '"herbert-kara/NiN"'],
        'v2rayN/ServiceLib/Services/UpdateService.cs': ['NiNRelease.SelectTag'],
        'v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs': ['ServerCountryService.Instance.ResolveAsync', 'ServerFlaggedService.Instance.ResolveAsync', 'LookupServerFlagsAsync'],
        'v2rayN/ServiceLib/Models/Dto/ProfileItemModel.cs': ['ServerCountryCode', 'CountryCode'],
        'v2rayN/v2rayN/Views/ProfilesView.xaml': ['<base:MyDGCountryColumn', 'btnRefreshServerFlags'],
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
    print('NiN identity, updater, flags, and automatic lookup guards passed')


if __name__ == '__main__':
    check()
