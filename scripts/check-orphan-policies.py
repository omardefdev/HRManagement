#!/usr/bin/env python3
"""Fail CI when docs/**/*.json is not registered in policy-binding-registry.json."""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REGISTRY = ROOT / "docs" / "policy-binding-registry.json"
DOCS = ROOT / "docs"

EXCLUDE_PARTS = {
    "rel-compat/openapi-baseline",
    "DATABASE_SCHEMA_INVENTORY.json",
}


def main() -> int:
    if not REGISTRY.exists():
        print(f"Missing registry: {REGISTRY}")
        return 1

    registry = json.loads(REGISTRY.read_text(encoding="utf-8"))
    registered = {entry["path"].replace("\\", "/") for entry in registry.get("policies", [])}

    orphans: list[str] = []
    for path in sorted(DOCS.rglob("*.json")):
        rel = path.relative_to(ROOT).as_posix()
        if any(part in rel for part in EXCLUDE_PARTS):
            continue
        if rel not in registered:
            orphans.append(rel)

    if orphans:
        print("Unregistered policy JSON files (add to docs/policy-binding-registry.json):")
        for o in orphans:
            print(f"  - {o}")
        return 1

    print(f"All {len(registered)} registered policy files present; no unregistered docs JSON.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
