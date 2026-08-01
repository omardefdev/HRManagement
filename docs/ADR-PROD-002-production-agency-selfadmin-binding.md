# ADR-PROD-002 — Agency-selfadmin production binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-002 (card 81) |
| Date | 2026-07-30 |
| Dependencies | IAM-003 Satisfied; TEN-003 Satisfied; PROD-001 Satisfied |

## Context

PROD-002 had no prior pack. This binding creates the agency-selfadmin matrix and loads it at host startup/CI so Staging/Production cannot claim live self-admin, mutate national controls, administer another tenant, or apply sensitive changes without four-eyes/rollback/audit.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/prod-agency/agency-selfadmin-matrix.json` |
| Runtime | `ProdAgencyOptions` + `IProdAgencyProvider` + `IAgencySelfAdminClaimGuard` |
| Live self-admin | Default `LiveAgencySelfAdminClaim=false` until PROD-002a–e |
| Soft blockers | live-agency-selfadmin / live-delegated-admin-ui / cross-tenant-admin / ONB-001 Blocked; PROD-003 NotStarted |
| Cross sync | PROD-001 + MIG-007 matrices: PROD-002 → Satisfied |

## Consequences

- Lab harness only until live-agency-selfadmin is raised.
- Live UI and ONB-001/PROD-003 joins remain out of scope.
