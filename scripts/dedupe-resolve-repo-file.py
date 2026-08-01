#!/usr/bin/env python3
"""Replace duplicated ResolveRepoFile helpers with HRManagement.TestSupport.RepoPaths."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TESTS = ROOT / "src" / "Tests" / "HRManagement.UnitTests"

METHOD = re.compile(
    r"\n    private static string ResolveRepoFile\(params string\[\] parts\)\r?\n"
    r"    \{\r?\n"
    r"        var dir = new DirectoryInfo\(AppContext\.BaseDirectory\);\r?\n"
    r"        while \(dir is not null\)\r?\n"
    r"        \{\r?\n"
    r"            var candidate = Path\.Combine\(new\[\] \{ dir\.FullName \}\.Concat\(parts\)\.ToArray\(\)\);\r?\n"
    r"            if \(System\.IO\.File\.Exists\(candidate\)\)\r?\n"
    r"                return candidate;\r?\n"
    r"            dir = dir\.Parent;\r?\n"
    r"        \}\r?\n"
    r"        throw new FileNotFoundException\(string\.Join\(\"/\", parts\)\);\r?\n"
    r"    \}\r?\n",
    re.MULTILINE,
)


def main() -> None:
    updated = 0
    for path in TESTS.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        if "private static string ResolveRepoFile" not in text:
            continue
        new_text = METHOD.sub("\n", text)
        if new_text == text:
            continue
        if "using HRManagement.TestSupport;" not in new_text:
            idx = new_text.find("\n", new_text.find("namespace "))
            new_text = new_text[: idx + 1] + "using HRManagement.TestSupport;\n" + new_text[idx + 1 :]
        new_text = new_text.replace("ResolveRepoFile(", "RepoPaths.Resolve(")
        path.write_text(new_text, encoding="utf-8")
        updated += 1
    print(f"Updated {updated} files")


if __name__ == "__main__":
    main()
