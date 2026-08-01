#!/usr/bin/env python3
"""GOV-001-C: retention dry-run — reports candidates without deleting."""
from __future__ import annotations

import json
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_json(path: Path) -> dict:
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return {}


def main() -> int:
    gw = load_json(ROOT / "src/Gateway/HRManagement.Gateway/appsettings.json")
    audit = gw.get("AuditRetention") or {}
    enabled = bool(audit.get("Enabled"))
    allow_purge = bool(audit.get("AllowPurge"))
    days = int(audit.get("RetentionDays") or 0)

    report = {
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "card": "GOV-001",
        "mode": "dry-run",
        "policies": [
            {
                "policyId": "ret-audit-gateway",
                "enabled": enabled,
                "allowPurge": allow_purge,
                "retentionDays": days,
                "actionIfExecuted": "DELETE gateway_audit_entries older than cutoff",
                "blockedReason": None
                if allow_purge
                else "AllowPurge=false (safe default)",
            },
            {
                "policyId": "ret-inbox",
                "enabled": True,
                "retentionDays": 90,
                "actionIfExecuted": "DELETE IntegrationMessageReceipt older than cutoff",
                "note": "Per-service InboxOptions; dry-run does not query DBs",
            },
            {
                "policyId": "ret-files-purge",
                "actionIfExecuted": "Purge soft-deleted files past RetainUntil without LegalHold",
                "note": "See FILE_LIFECYCLE.md",
            },
            {
                "policyId": "ret-employee-lifecycle",
                "actionIfExecuted": "Anonymize separated employees without LegalHold",
                "note": "DATA-002 TryAnonymize — not auto-purged here",
            },
        ],
        "inventoryRef": "docs/data-classification/inventory.json",
        "scheduleRef": "docs/GOV_001_RETENTION_SCHEDULE.md",
    }

    # Safety: fail dry-run validation if someone enabled purge without documenting hold check
    if enabled and allow_purge:
        report["warnings"] = [
            "AllowPurge=true — ensure legal holds checked before hosting retention job runs"
        ]

    out_dir = ROOT / "artifacts"
    out_dir.mkdir(exist_ok=True)
    out = out_dir / "gov001-retention-dry-run.json"
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, ensure_ascii=False))
    print(f"Wrote {out}", file=sys.stderr)

    # Soft gate: Enabled+AllowPurge without HOLD_ACK env is a warning exit 0;
    # hard fail only if HOLD_ACK missing AND REQUIRE_HOLD_ACK=true
    if enabled and allow_purge and not __import__("os").environ.get("HOLD_ACK"):
        if __import__("os").environ.get("REQUIRE_HOLD_ACK", "").lower() in ("1", "true", "yes"):
            print("FAIL: AllowPurge=true requires HOLD_ACK=1 (GOV-001)", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
