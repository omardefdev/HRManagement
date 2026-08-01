# ADR-PROD-002 — Agency self-admin within governance (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-002 (card 81) |
| Date | 2026-07-30 |
| Dependencies | IAM-003 Satisfied; TEN-003 Satisfied; PROD-001 Satisfied |

## Context

PROD-001 provides versioned policy contracts. IAM-003/TEN-003 provide roles and tenant isolation. There is no signed contract for delegated agency admin (structure/roles/templates/calendars/notifications/quotas) without DBA, with immutable national controls, cross-tenant forbid, four-eyes on sensitive changes, rollback/audit, and preview — without claiming a live self-admin UI.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/prod-agency/agency-selfadmin-matrix.json` |
| Depth | Lab agency-selfadmin gate pack + harness; not live UI |
| Soft blockers | live-agency-selfadmin; live-delegated-admin-ui; cross-tenant-admin; ONB-001; PROD-003 |
| Closure | Pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| PROD-002a | Live agency self-admin UI |
| PROD-002b | Live delegated structure/roles |
| PROD-002c | Live quotas/calendars/notifications |
| PROD-002d | ONB-001 join |
| PROD-002e | PROD-003 join |
