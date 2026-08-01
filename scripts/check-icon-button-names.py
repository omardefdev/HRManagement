#!/usr/bin/env python3
"""UX-002-E: fail if Index/Shared views have icon-only controls without accessible name."""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src"

TAG_RE = re.compile(
    r"<(?P<tag>a|button)(?P<pre>[^>]*?)>"
    r"(?P<body>\s*(?:<i[^>]*class=\"[^\"]*(?:fas|far|fab)[^\"]*\"[^>]*>\s*</i>\s*)+)"
    r"\s*</(?P=tag)>",
    re.I | re.S,
)


def has_name(pre: str, body: str) -> bool:
    if re.search(r"aria-label\s*=", pre, re.I):
        return True
    if re.search(r"title\s*=", pre, re.I):
        return True
    if re.sub(r"<[^>]+>", "", body).strip():
        return True
    if "visually-hidden" in body or "sr-only" in body:
        return True
    return False


def main() -> int:
    files = list(ROOT.rglob("**/Index.cshtml"))
    shared = ROOT / "Shared" / "HRManagement.Web.Shared" / "Views"
    if shared.exists():
        files.extend(shared.rglob("*.cshtml"))

    fail = 0
    for f in files:
        if "wwwroot" in f.parts:
            continue
        text = f.read_text(encoding="utf-8", errors="replace")
        for m in TAG_RE.finditer(text):
            if not has_name(m.group("pre"), m.group("body")):
                line = text[: m.start()].count("\n") + 1
                print(f"FAIL {f.relative_to(ROOT.parent)}:{line} icon-only control without accessible name")
                fail += 1
    if fail:
        print(f"Icon button name check failed ({fail} findings). UX-002.")
        return 1
    print("Icon button accessible-name check OK (UX-002).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
