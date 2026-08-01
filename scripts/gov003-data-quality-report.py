#!/usr/bin/env python3
"""GOV-003-D: data quality report with threshold evaluation (fixture-friendly)."""
from __future__ import annotations

import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_THRESHOLDS = {
    "completeness_violations_max": 0,
    "national_id_duplicates_max": 0,
    "orphan_employee_ids_max": 0,
    "open_duplicate_cases_warn": 10,
}


def evaluate(metrics: dict, thresholds: dict | None = None) -> dict:
    t = {**DEFAULT_THRESHOLDS, **(thresholds or {})}
    failures = []
    warnings = []

    if metrics.get("completeness_violations", 0) > t["completeness_violations_max"]:
        failures.append("completeness_violations")
    if metrics.get("national_id_duplicates", 0) > t["national_id_duplicates_max"]:
        failures.append("national_id_duplicates")
    if metrics.get("orphan_employee_ids", 0) > t["orphan_employee_ids_max"]:
        failures.append("orphan_employee_ids")
    if metrics.get("open_duplicate_cases", 0) > t["open_duplicate_cases_warn"]:
        warnings.append("open_duplicate_cases")

    return {
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "card": "GOV-003",
        "metrics": metrics,
        "thresholds": t,
        "failures": failures,
        "warnings": warnings,
        "ok": len(failures) == 0,
    }


def main() -> int:
    fixture = os.environ.get("GOV003_DQ_FIXTURE")
    if fixture:
        metrics = json.loads(Path(fixture).read_text(encoding="utf-8"))
    else:
        # Offline default: clean fixture (CI without DB).
        metrics = {
            "completeness_violations": 0,
            "national_id_duplicates": 0,
            "orphan_employee_ids": 0,
            "open_duplicate_cases": 0,
            "note": "offline defaults — set GOV003_DQ_FIXTURE or wire DB later",
        }

    report = evaluate(metrics)
    out_dir = ROOT / "artifacts"
    out_dir.mkdir(exist_ok=True)
    out = out_dir / "gov003-dq-report.json"
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, ensure_ascii=False))
    print(f"Wrote {out}", file=sys.stderr)
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
