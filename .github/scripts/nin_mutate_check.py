"""Mutation check for the nin.20 sub-switch fixes. Run from repo root.

Re-applies each defect the guards claim to catch, asserts the guard fails, and
restores. Exits non-zero if any mutation is missed.
"""
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
VM = ROOT / "v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs"
CH = ROOT / "v2rayN/ServiceLib/Handler/ConfigHandler.cs"
GUARD = ".github/scripts/test_nin_sub_switch_guard.py"
DEADLOCK = ".github/scripts/test_nin_ui_deadlock.py"


def run(script):
    return subprocess.run([sys.executable, script], cwd=ROOT,
                          capture_output=True, text=True).returncode


MUTATIONS = [
    ("stale subid used for the rebuild",
     VM,
     "        var subid = SelectedSub?.Id ?? _config.SubIndexId;",
     "        var subid = _config.SubIndexId;",
     GUARD),
    ("GetProfileItemsEx ignores its subid argument",
     VM,
     "AppManager.Instance.ProfileModels(subid, filter)",
     "AppManager.Instance.ProfileModels(_config.SubIndexId, filter)",
     GUARD),
    ("whole-table server steal from another sub",
     CH,
     "        if (lstProfile.Count == 0)\n        {\n            return -1;\n        }\n"
     "        return await SetDefaultServerIndex(config, lstProfile.FirstOrDefault(t => t.Port > 0)?.IndexId);",
     "        if (lstProfile.Count > 0)\n        {\n"
     "            return await SetDefaultServerIndex(config, lstProfile.FirstOrDefault(t => t.Port > 0)?.IndexId);\n"
     "        }\n\n        var item = await SQLiteHelper.Instance.TableAsync<ProfileItem>().FirstOrDefaultAsync(t => t.Port > 0);\n"
     "        return await SetDefaultServerIndex(config, item?.IndexId);",
     GUARD),
    ("sub-switch guard removed",
     VM,
     "        await _subSwitchSemaphore.WaitAsync();",
     "",
     GUARD),
    ("stale-pass check dropped after refresh",
     VM,
     "            await RefreshServers();\n\n            if (Volatile.Read(ref _subSwitchPass) != pass)\n"
     "            {\n                return;\n            }\n\n            await ProfilesFocusInteraction",
     "            await RefreshServers();\n\n            await ProfilesFocusInteraction",
     GUARD),
    ("lookup awaited -> UI deadlock",
     VM,
     "        _ = LookupServerFlagsAsync(ProfileItems.ToList());",
     "        await LookupServerFlagsAsync(ProfileItems.ToList());",
     DEADLOCK),
]

failed = False
for name, path, old, new, script in MUTATIONS:
    orig = path.read_text(encoding="utf-8-sig")
    mutated = orig.replace(old, new)
    if mutated == orig:
        print(f"SKIP (anchor not found): {name}")
        failed = True
        continue
    path.write_text(mutated, encoding="utf-8-sig")
    caught = run(script) != 0
    path.write_text(orig, encoding="utf-8-sig")
    print(f"{'CAUGHT' if caught else 'MISSED'}: {name}")
    if not caught:
        failed = True

# every guard must be green again on the restored tree
restored = all(run(s) == 0 for s in {GUARD, DEADLOCK})
print("restored tree:", "OK" if restored else "BROKEN")
sys.exit(1 if failed or not restored else 0)
