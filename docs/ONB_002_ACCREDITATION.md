# ONB-002 — Agency Accreditation Waves & Readiness Checklist

**Card:** ONB-002
**Status:** Done
**Priority:** P1-National / L
**Owner:** Program Management + Agency Liaison + Training

## Overview

Defines the wave-based agency accreditation process for onboarding agencies to the national HR platform. Each agency must pass a readiness checklist before entering a deployment wave. The checklist covers data readiness, infrastructure, training, governance, and operational prerequisites.

## Wave Readiness Checklist

The machine-readable checklist is at `docs/onb/wave-readiness-checklist.json`.

### Wave Progression

| Wave | Scope | Entry Criteria |
|------|-------|----------------|
| Pilot | 1–2 agencies, limited functions | Full readiness checklist + sponsor sign-off |
| Phase 1 | 3–5 agencies, core functions | Pilot lessons incorporated + readiness checklist |
| Phase 2 | Sector rollout (e.g., all ministries) | Phase 1 stable ≥30 days + sector readiness |
| National | All 14 governorates, all agencies | Phase 2 stable ≥30 days + national readiness |

### Readiness Dimensions

| ID | Dimension | Description |
|----|-----------|-------------|
| RD-01 | Data Migration | Legacy data extracted, transformed, loaded, and reconciled |
| RD-02 | Infrastructure | Network, devices, and connectivity meet minimum standards |
| RD-03 | Training | ≥90% staff trained and assessed per TRN-001 |
| RD-04 | Governance | Agency admin appointed, roles configured, workflows activated |
| RD-05 | Communication | Staff notified ≥14 days; union engagement per TRN-002 |
| RD-06 | Support | Help desk staffed; Tier-1 Arabic support available |
| RD-07 | Rollback Plan | Documented rollback procedure reviewed and rehearsed |
| RD-08 | UAT Sign-off | Agency UAT completed per UAT-001 |

### Go/No-Go Decision

- Readiness assessment completed by Program Management
- All mandatory dimensions must be "Ready" or "Ready-with-conditions"
- Conditions documented with remediation owner and deadline
- No-Go decision halts wave for the agency; does not block other agencies
- Blast radius and rollback scope defined per wave
- 30-day monitoring period after each wave before expansion

## Dependencies

TRN-001, TRN-002, UAT-001, MIG-006, BCP-001

## Artifacts

- `docs/onb/wave-readiness-checklist.json`
- `scripts/onb002-validate.ps1`
- `Onb002AccreditationTests`
