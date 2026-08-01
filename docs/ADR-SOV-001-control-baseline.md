# ADR-SOV-001 — National control baseline / ATO gate pack

| Field | Value |
|---|---|
| Status | Accepted |
| Card | SOV-001 (card 87) |
| Date | 2026-07-30 |
| Dependencies | STR-002/SUP-001/GOV-001 Satisfied |

## Context

No signed contract for local-law tailoring + NIST/ISO/ASVS/SSDF crosswalk, impact categorization, full control coverage with owner/evidence, dated POA&M, signed exceptions, authorization boundary, and independent assessment — without claiming live independent ATO.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/sov-baseline/control-baseline-matrix.json` |
| Depth | Lab control-baseline gate pack; not live ATO |
| Soft blockers | live-independent-ato / continuous-assessment-workers / live-poam-system Blocked; SYR-001/NAT-001 NotStarted; SOV-002 Satisfied (data-sovereignty binding) |
| Closure | Pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| SOV-001a | Live independent ATO |
| SOV-001b | Continuous assessment workers |
| SOV-001c | Live POA&M system |
| SOV-001d | SYR-001 corpus join |
| SOV-001e | SOV-002 expansion |
