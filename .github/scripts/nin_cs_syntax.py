"""Lightweight C# syntax sanity check for the files NiN edits.

Catches the class of mistake a Python-only change is prone to: an unbalanced
delimiter in a method chain (a stray ')' before '&&' broke the whole ServiceLib
build and was only caught in CI). Compilation still happens in GitHub Actions;
this is the fast pre-flight that must pass before a tag is pushed.
"""
import re
import sys
from pathlib import Path

# Files NiN customises or edits. Kept explicit so upstream churn elsewhere in
# the tree does not make this noisy.
WATCHED = [
    "v2rayN/ServiceLib/Common/NiNRelease.cs",
    "v2rayN/ServiceLib/Common/ProfileCountry.cs",
    "v2rayN/ServiceLib/Global.cs",
    "v2rayN/ServiceLib/Handler/ConnectionHandler.cs",
    "v2rayN/ServiceLib/Handler/ConfigHandler.cs",
    "v2rayN/ServiceLib/Models/Dto/ProfileItemModel.cs",
    "v2rayN/ServiceLib/Models/Dto/SemanticVersion.cs",
    "v2rayN/ServiceLib/Services/ServerCountryService.cs",
    "v2rayN/ServiceLib/Services/ServerFlaggedService.cs",
    "v2rayN/ServiceLib/Services/UpdateService.cs",
    "v2rayN/ServiceLib/ViewModels/CheckUpdateViewModel.cs",
    "v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs",
    "v2rayN/v2rayN/Base/MyDGCountryColumn.cs",
    "v2rayN/v2rayN/Base/MyDGFlagColumn.cs",
    "v2rayN/v2rayN/Common/NiNSwatches.cs",
    "v2rayN/v2rayN/Converters/FlagStatusConverter.cs",
    "v2rayN/v2rayN/Views/ProfilesView.xaml.cs",
    "v2rayN/v2rayN.Desktop/Converters/FlagStatusConverter.cs",
    "v2rayN/ServiceLib.Tests/Models/NiNReleaseTests.cs",
    "v2rayN/ServiceLib.Tests/Services/ServerFlaggedServiceTests.cs",
]

PAIRS = {"(": ")", "{": "}", "[": "]"}


def strip_noise(text):
    """Blank out comments, verbatim strings and regular strings."""
    out = []
    i, n = 0, len(text)
    while i < n:
        ch = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if ch == "/" and nxt == "/":
            while i < n and text[i] != "\n":
                i += 1
        elif ch == "/" and nxt == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                if text[i] == "\n":
                    out.append("\n")
                i += 1
            i += 2
        elif ch == "@" and nxt == '"':
            out.append(' ')
            i += 2
            while i < n:
                if text[i] == '"' and text[i + 1] == '"':
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                i += 1
        elif ch == '"':
            out.append(' ')
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                if text[i] == "\n":
                    out.append("\n")
                i += 1
        elif ch == "'":
            # Character literal: '(', '{', '[' etc. are not delimiters.
            out.append(" ")
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == "'":
                    i += 1
                    break
                i += 1
        else:
            out.append(ch)
            i += 1
    return "".join(out)


def check_file(path, root):
    p = root / path
    if not p.exists():
        return [f"{path}: MISSING"]
    clean = strip_noise(p.read_text(encoding="utf-8-sig"))
    problems = []
    stack = []
    line = 1
    for ch in clean:
        if ch == "\n":
            line += 1
        elif ch in PAIRS:
            stack.append((ch, line))
        elif ch in PAIRS.values():
            if not stack:
                problems.append(f"{path}:{line}: unexpected '{ch}'")
            else:
                opener, opened_at = stack.pop()
                if PAIRS[opener] != ch:
                    problems.append(
                        f"{path}:{line}: '{ch}' closes '{opener}' opened at line {opened_at}")
    for opener, opened_at in stack:
        problems.append(f"{path}:{opened_at}: unclosed '{opener}'")
    return problems


def check(root=None):
    root = Path(root) if root else Path(__file__).resolve().parents[2]
    problems = []
    for rel in WATCHED:
        problems.extend(check_file(rel, root))
    if problems:
        raise RuntimeError("C# syntax check failed:\n  " + "\n  ".join(problems))
    return f"C# syntax check passed for {len(WATCHED)} customized files"


if __name__ == "__main__":
    try:
        print(check())
    except RuntimeError as exc:
        print(exc)
        sys.exit(1)
