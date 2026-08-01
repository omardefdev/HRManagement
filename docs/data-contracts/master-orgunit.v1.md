# Data contract — Master OrgUnit v1 (GOV-003)

| Item | Value |
|---|---|
| SoR | HR.Core `OrgUnit` |
| Contract | master-orgunit.v1 |

## Logical fields

| Field | Writer | Notes |
|---|---|---|
| Id, TenantId, Code, NameAr/En | HR.Core | Code unique within steward policy |
| ParentOrgUnitId | HR.Core | |
| MaterializedPath | HR.Core | PERF-003 |

## Forbidden writers

- Payroll/Contracts/Movements **OrgUnitSnapshot** tables are copies — must not invent new Codes or rewrite path independently of HR.Core publish/sync.

## Freshness expectation

After OrgUnit change in HR.Core, snapshots refresh on next sync/event; DQ dimension `snapshot_freshness` flags lag beyond configured hours (default alert-only in GOV-003 report).
