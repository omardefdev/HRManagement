# ADR-STR-006 — Permanent ownership, funding, skills, and vendors

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-006 (card 64) |
| Date | 2026-07-30 |
| Dependencies | STR-005 Satisfied; SOV-007/FIN-001/SRE-001 soft-blocked (circular) |

## Context

STR-005 defines program decision boards. REL-003 has ops service owners and an on-call template that is not national 24x7. There is no machine ownership quad (product/service/data/security), no bus-factor gate, no 5-year ops/security/backup funding stub, and no vendor clause matrix for RTO / incident disclosure / exit. FIN-001 and SRE-001 depend on STR-006 while STR-006 lists them as deps — close with stubs and soft blockers.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/str-ownership/ownership-matrix.json` |
| Depth | **Ownership/sustainment gate pack** — docs + fixtures + validate; not live SRE desk or full TCO workbook |
| Ownership | Quad roles per critical component; busFactor >= 2 |
| Staffing | Template extending REL-003; `national24x7Claim: false` → SRE-001 |
| Budget | 5y stub; must fund ops/security/backup; forbid dev-only → FIN-001 |
| Vendors | Required clauses RTO + IncidentDisclosure + Exit; stubs → SOV-007/SUP-004 |
| Soft blockers | SOV-007=Satisfied (vendor-exit binding; live drill = VEN-001); FIN-001=NotStarted; SRE-001=Blocked; STR-005=Satisfied |
| Closure | Done (ownership/sustainment gate pack) + documented blockers |
| Out of scope | SRE-001 live 24x7; FIN-001 independent finance review; SOV-007 clean-room exit; Domain SuccessionPlan/Vendor CRUD |

## Blocker register

| Id | Status | Note |
|---|---|---|
| STR-005 | Satisfied | Governance boards Done |
| SOV-007 | Satisfied | Vendor-exit binding Done; live exit drill = VEN-001 |
| FIN-001 | NotStarted | Full TCO workbook consumes this stub |
| SRE-001 | Blocked | National 24x7 desk; roster template + simulation only |

## Consequences

- SUP-003/004, PGM-001, SRE-001, FIN-001 consume this pack.
- Validate fails on critical busFactor&lt;2, dev-only budget, vendor missing Exit/RTO/disclosure, or `national24x7Claim: true`.
