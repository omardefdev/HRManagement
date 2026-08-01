#!/usr/bin/env python3
"""GOV-001-D: fail on suspicious production dump artifacts in the repo."""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

ALLOW_DIRS = {
    "testdata/e2e",
    "testdata/nfr",
    "docs",
    "artifacts",
}

SUSPICIOUS = re.compile(
    r"(prod[-_]?dump|production[-_]?dump|prod[-_]?backup|production\.sql|prod_data\.sql)",
    re.I,
)


def main() -> int:
    if os.environ.get("ALLOW_PROD_DATA_COPY", "").lower() in ("1", "true", "yes"):
        print("ALLOW_PROD_DATA_COPY set — skipping non-prod dump gate.")
        return 0

    fail = 0
    for path in ROOT.rglob("*"):
        if not path.is_file():
            continue
        rel = path.relative_to(ROOT).as_posix()
        if any(rel.startswith(a + "/") or rel == a for a in ("bin", "obj", ".git", "node_modules")):
            continue
        if any(rel.startswith(a + "/") for a in ALLOW_DIRS):
            # still block obvious prod dumps even under testdata
            if "e2e" in rel and path.suffix.lower() in {".json", ".md"}:
                continue
        name = path.name
        if SUSPICIOUS.search(name) or SUSPICIOUS.search(rel):
            # allowlisted synthetic seeds
            if rel.startswith("testdata/") and path.suffix.lower() in {".json", ".md", ".csv"}:
                continue
            print(f"FAIL: suspicious production data artifact: {rel}")
            fail += 1

    if fail:
        print("See docs/NON_PROD_DATA_POLICY.md (GOV-001-D).")
        return 1
    print("Non-prod data policy check OK (GOV-001).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
