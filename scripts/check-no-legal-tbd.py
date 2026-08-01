#!/usr/bin/env python3
"""GOV-002-B + STR-002: fail if LEGAL_TBD remains in release-path sources."""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

SCAN_GLOBS = [
    "src/**/*.cs",
    "docs/PAYROLL_RULES.md",
]

CATALOG_PATH = ROOT / "docs" / "str-legal" / "control-catalog.json"

# Historical mentions allowed only in ADR / verification with this exact phrase prefix.
ALLOW_SUBSTRINGS = (
    "formerly LEGAL_TBD",
    "former LEGAL_TBD",
    "removed LEGAL_TBD",
    "إزالة LEGAL_TBD",
    "لا `LEGAL_TBD`",
    "لا LEGAL_TBD",
)


def iter_files():
    for pattern in SCAN_GLOBS:
        yield from ROOT.glob(pattern)


def line_allowed(line: str) -> bool:
    return any(a in line for a in ALLOW_SUBSTRINGS)


def scan_sources() -> list[str]:
    fails: list[str] = []
    for path in iter_files():
        if not path.is_file():
            continue
        rel = path.relative_to(ROOT).as_posix()
        if "/obj/" in rel or "/bin/" in rel:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        if "LEGAL_TBD" not in text:
            continue
        for i, line in enumerate(text.splitlines(), 1):
            if "LEGAL_TBD" not in line:
                continue
            if line_allowed(line):
                continue
            fails.append(f"{rel}:{i}: {line.strip()}")
    return fails


def scan_catalog() -> list[str]:
    fails: list[str] = []
    if not CATALOG_PATH.is_file():
        fails.append("docs/str-legal/control-catalog.json missing")
        return fails
    doc = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))
    for c in doc.get("controls", []):
        if c.get("status") != "Implemented":
            continue
        blob = " ".join(str(c.get(k) or "") for k in ("evidencePath", "testRef"))
        if "LEGAL_TBD" in blob.upper():
            rid = c.get("requirementId", "?")
            fails.append(
                f"docs/str-legal/control-catalog.json: Implemented {rid} must not reference LEGAL_TBD"
            )
    return fails


def main() -> int:
    fails = scan_sources() + scan_catalog()
    if fails:
        print("FAIL: LEGAL_TBD found (GOV-002 / STR-002). Bind to decisionId instead.")
        for f in fails:
            print(f)
        return 1
    print("No LEGAL_TBD in release path (GOV-002 / STR-002).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
