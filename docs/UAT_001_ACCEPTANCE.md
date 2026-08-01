# UAT-001 — Formal User Acceptance Testing & Sign-Off

**Card:** UAT-001
**Status:** Done
**Priority:** P1-National / L
**Owner:** Product Council + Agency Representatives + QA

## Overview

Establishes the formal user acceptance testing (UAT) framework that bridges development completion and production authorization. Every capability must pass structured acceptance testing by designated agency representatives before it can be included in a production wave. UAT is evidence-based, traceable to requirements, and produces signed acceptance artifacts.

## Requirement-Test-Evidence Matrix

The machine-readable matrix is at `docs/uat/requirement-test-evidence-matrix.json`.

### UAT Process

| Phase | Description |
|-------|-------------|
| Planning | Map requirements to test scenarios; assign testers per agency archetype |
| Environment | Provision UAT environment with production-like data (anonymized) |
| Execution | Testers execute scenarios; record pass/fail with evidence |
| Defect Triage | Classify defects as blocker/critical/major/minor; blockers halt sign-off |
| Re-test | Fixed defects re-verified by original tester |
| Sign-off | Authorized representative signs acceptance per capability |

### Acceptance Criteria Categories

| ID | Category | Description |
|----|----------|-------------|
| AC-01 | Functional correctness | Business logic matches approved requirements |
| AC-02 | Data integrity | No data loss, corruption, or silent truncation |
| AC-03 | Accessibility | WCAG 2.1 AA compliance for Arabic RTL |
| AC-04 | Performance | Response times within SLO under realistic load |
| AC-05 | Security | No unauthorized access paths discovered during UAT |
| AC-06 | Localization | Arabic/English display correct; numbers/dates localized |
| AC-07 | Regulatory | Payroll/tax/insurance calculations match golden cases |

### Sign-Off Authority

- Each agency archetype designates an authorized UAT signer
- Sign-off is per capability, not blanket
- Conditional sign-off allowed with documented exceptions and remediation timeline
- No capability enters production wave without signed acceptance or documented waiver

## Traceability

- Every requirement has at least one test scenario
- Every test scenario has execution evidence (screenshot, log, or automated result)
- Evidence linked to requirement ID and release version
- Gap analysis report generated automatically from matrix

## Dependencies

PRD-001, UXR-001, QA-002, EVD-001

## Artifacts

- `docs/uat/requirement-test-evidence-matrix.json`
- `scripts/uat001-validate.ps1`
- `Uat001AcceptanceTests`
