# ADR-STR-005 — Program governance and decision boards

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-005 (card 63) |
| Date | 2026-07-30 |
| Dependencies | None (hard); soft: SYD-017, PGM-001 consumers |

## Context

STR-001..004 define product envelope, legal controls, modules, and no-fork policy. There is no machine decision-rights matrix, no program decision/risk registers with exception expiry, and no proof that Go-Live cannot be declared by engineering alone. File 14 (SYD-017) still has open signatory names. PGM-001 and SCM-001 depend on this governance baseline.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/str-governance/governance-matrix.json` — DecisionRights / Boards / AdrRequired / ExceptionExpiry / GoLiveMultiSeat / ConflictEscalation |
| Depth | **Governance gate pack** — docs + JSON + fixtures + validate harness; not a live PMO or approval workflow engine |
| Boards | Architecture, Security, Data, Change, Program — each with quorum and owner ≠ reviewer |
| Decision log | `docs/str-governance/decision-log.json` (program scope; distinct from GOV-002 legal payroll register) |
| Risk register | `docs/str-governance/risk-register.json` |
| Exceptions | Declared + temporary + `expiresOn`; expired without renew = validate FAIL |
| Go-Live | Multi-seat: Sponsor + Security + Architecture (Data when required); eng-only = FAIL |
| Simulations | Law change / incident / delay fixtures under `testdata/str005/scenarios/` |
| Soft blockers | SYD-017 = Blocked (named signatories); PGM-001 = NotStarted (schedule consumer) |
| Closure | Done (governance gate pack) + documented soft blockers |
| Out of scope | PGM-001 critical path; live approval tool; SYD-017 person assignment; replacing GOV-002 / REL-003; full SCM-001 |

## Blocker register

| Id | Status | Note |
|---|---|---|
| SYD-017 | Blocked | Named Go-Live signatories still OPEN in file 14 |
| PGM-001 | NotStarted | Program schedule / XL split consumes STR-005; not built here |

## Consequences

- PGM-001, SCM-001, STR-006, SAN-001 escalate to or consume these boards and registers.
- Validate fails if owner == reviewer, exception past expiry, or eng-only Go-Live evidence is accepted.
- Role placeholders remain until SYD-017 closes; seats are role-based, not person-named.
