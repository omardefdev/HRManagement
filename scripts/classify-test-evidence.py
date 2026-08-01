#!/usr/bin/env python3
"""Classify test evidence: behavior vs artifact-gate vs architecture-fitness.

Emits docs/qa/test-count-registry.json and docs/qa/reopened-cards-registry.json.
Used by CI to publish separated counts (AUDIT_PROTOCOL §2).
"""
from __future__ import annotations

import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UNIT_TESTS = ROOT / "src" / "Tests" / "HRManagement.UnitTests"
INTEGRATION = ROOT / "src" / "Tests" / "HRManagement.IntegrationTests"
QA_DIR = ROOT / "docs" / "qa"
COVERAGE_FLOORS = ROOT / "coverage-floors.json"

# Card prefixes whose closure must be behavioral (plan phase0-reopen-cards).
REOPEN_CARD_PREFIXES = (
    "NAT-00",
    "SYR-",
    "REL-00",
    "SEC-001",
    "SEC-004",
    "BLD-001",
    "HARD-001",
    "IAM-005",
    "PRV-001",
    "VEN-001",
    "UAT-001",
    "TRN-001",
    "SAN-001",
    "PHY-001",
    "QA-005",
)

PRODUCT_ASSEMBLY_MARKERS = (
    "Payroll.",
    "HR.Core.",
    "HR.Contracts.",
    "HR.Health.",
    "HR.Movements.",
    "Compliance.",
    "HRManagement.",
    "Records.Archive.",
    "Auth.Directory.",
    "Admin.Logistics.",
    "IDCard.",
    "IT.Infra.",
)

ARCHITECTURE_FILE_PATTERNS = (
    "ArchitectureTests",
    "MassAssignmentArchitecture",
    "ApprovalBypassArchitecture",
    "BolaArchitecture",
)

ARTIFACT_MARKERS = (
    "RepoPaths.Resolve",
    "ResolveRepoFile(",
    "docs/",
    "scripts/",
    "-validate.ps1",
    "ProcessStartInfo",
    "control-evidence-index",
    "checklist.json",
)


def _count_test_methods(text: str) -> int:
    return len(re.findall(r"\[(?:Fact|Theory)\]", text))


def _classify_file(path: Path, text: str) -> str:
    name = path.name
    if any(p in name for p in ARCHITECTURE_FILE_PATTERNS):
        return "architectureFitness"

    has_product = any(m in text for m in PRODUCT_ASSEMBLY_MARKERS)
    has_artifact = any(m in text for m in ARTIFACT_MARKERS)

    if has_product and not has_artifact:
        return "behavior"
    if has_artifact and not has_product:
        return "artifactGate"
    if has_product and has_artifact:
        return "mixed"
    return "artifactGate"


def _card_ids_from_file(path: Path) -> list[str]:
    stem = path.stem
    ids: list[str] = []
    # Nat003PayrollCertTests -> NAT-003
    for m in re.finditer(r"(?i)(nat|syr|rel|sec|bld|hard|iam|prv|ven|uat|trn|san|phy|qa)(\d{3})", stem):
        prefix = m.group(1).upper()
        num = m.group(2)
        ids.append(f"{prefix}-{num}")
    # Also match embedded SYR-011 style
    for m in re.finditer(r"(?i)(NAT|SYR|REL|SEC|BLD|HARD|IAM|PRV|VEN|UAT|TRN|SAN|PHY|QA)-(\d{3})", stem):
        ids.append(f"{m.group(1).upper()}-{m.group(2)}")
    return sorted(set(ids))


def _should_reopen(card_id: str) -> bool:
    return any(card_id.startswith(p) or card_id == p for p in REOPEN_CARD_PREFIXES)


def scan_tests() -> dict:
    files: list[dict] = []
    counts = {"behavior": 0, "artifactGate": 0, "architectureFitness": 0, "mixed": 0}

    for root in (UNIT_TESTS, INTEGRATION):
        if not root.exists():
            continue
        for path in sorted(root.rglob("*Tests.cs")):
            text = path.read_text(encoding="utf-8", errors="replace")
            kind = _classify_file(path, text)
            n = _count_test_methods(text)
            counts[kind] += n
            files.append(
                {
                    "path": path.relative_to(ROOT).as_posix(),
                    "kind": kind,
                    "testMethods": n,
                    "cardIds": _card_ids_from_file(path),
                }
            )

    return {"files": files, "counts": counts}


def build_reopened_registry(files: list[dict]) -> dict:
    by_card: dict[str, dict] = {}
    for f in files:
        for cid in f["cardIds"]:
            if cid not in by_card:
                by_card[cid] = {
                    "cardId": cid,
                    "testFiles": [],
                    "artifactGateMethods": 0,
                    "behaviorMethods": 0,
                    "reopenStatus": "Blocked" if _should_reopen(cid) else "N/A",
                    "reason": (
                        "دليل اختباري غير سلوكي — لا ينفّذ كود المنتج"
                        if _should_reopen(cid)
                        else None
                    ),
                }
            by_card[cid]["testFiles"].append(f["path"])
            if f["kind"] == "behavior":
                by_card[cid]["behaviorMethods"] += f["testMethods"]
            elif f["kind"] in ("artifactGate", "mixed"):
                by_card[cid]["artifactGateMethods"] += f["testMethods"]

    reopened = [c for c in by_card.values() if c["reopenStatus"] == "Blocked" and c["behaviorMethods"] == 0]
    reopened.sort(key=lambda x: x["cardId"])
    return {
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "reopenedCount": len(reopened),
        "cards": reopened,
    }


def build_behavior_coverage() -> dict:
    floors = json.loads(COVERAGE_FLOORS.read_text(encoding="utf-8"))
    assemblies = floors.get("gateAssemblies", [])
    rows = []
    for asm in assemblies:
        rows.append(
            {
                "assembly": asm,
                "floorPercent": floors.get("floors", {}).get(asm),
                "behaviorTestFiles": [],
                "gap": "Map domain types to behavioral tests in QA_RISK_TO_TEST_MATRIX.md",
                "priority": "high" if "Payroll" in asm or "Compliance" in asm else "medium",
            }
        )
    # Link known behavioral test files
    matrix_hints = {
        "Payroll.Domain": ["PayrollFinancialInvariantsTests.cs", "PayrollCalculationExtendedTests.cs"],
        "Payroll.Application": ["Frd001FraudEngineTests.cs"],
        "Compliance.Domain": ["ComplianceDomainRulesTests.cs"],
        "HR.Contracts.Domain": ["LeaveAndAdvanceRulesTests.cs"],
    }
    for row in rows:
        row["behaviorTestFiles"] = matrix_hints.get(row["assembly"], [])

    return {
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "gateAssemblies": rows,
        "note": "Line-coverage floors are not behavioral proof; each row needs linked behavioral tests.",
    }


def main() -> int:
    QA_DIR.mkdir(parents=True, exist_ok=True)
    scan = scan_tests()
    counts = scan["counts"]

    test_registry = {
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "behavior": counts["behavior"],
        "artifactGate": counts["artifactGate"],
        "architectureFitness": counts["architectureFitness"],
        "mixed": counts["mixed"],
        "totalMethods": sum(counts.values()),
        "note": "Publish behavior count separately from artifact/architecture gates (AUDIT_PROTOCOL §2).",
    }

    reopened = build_reopened_registry(scan["files"])
    behavior_cov = build_behavior_coverage()

    (QA_DIR / "test-count-registry.json").write_text(
        json.dumps(test_registry, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (QA_DIR / "reopened-cards-registry.json").write_text(
        json.dumps(reopened, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (QA_DIR / "behavior-coverage-by-gate-assembly.json").write_text(
        json.dumps(behavior_cov, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )

    print(
        f"Test evidence: behavior={counts['behavior']} "
        f"artifactGate={counts['artifactGate']} "
        f"architecture={counts['architectureFitness']} "
        f"mixed={counts['mixed']}"
    )
    print(f"Reopened cards registry: {reopened['reopenedCount']} cards")
    return 0


if __name__ == "__main__":
    sys.exit(main())
