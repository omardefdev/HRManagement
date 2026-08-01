# ADR-PROD-003 — Employee/manager self-service (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-003 (card 82) |
| Date | 2026-07-30 |
| Dependencies | IAM-003 Satisfied; FUN-001..006 Satisfied; UX-002 Satisfied; IAM-002 NotStarted (honest soft blocker) |

## Context

No signed contract for high-volume employee/manager self-service journeys (profile correction, leave, payslip/tax, documents, manager approvals, delegation, inbox) with correction visibility, manager scope, usage telemetry lab stub, privacy/device-session, and persona UAT — without claiming live ESS/MSS portals.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/prod-ess/employee-selfservice-matrix.json` |
| Depth | Lab employee-selfservice gate pack + harness; not live portals |
| Soft blockers | IAM-002 NotStarted; live-employee/manager-portal Blocked; live-usage-telemetry Blocked; ESS-001/MSS-001/PROD-004 NotStarted |
| Closure | Pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| PROD-003a | Live employee portal |
| PROD-003b | Live manager portal |
| PROD-003c | Live usage telemetry / KPI |
| PROD-003d | ESS-001 / MSS-001 join |
| PROD-003e | PROD-004 / NAT-005 join |
