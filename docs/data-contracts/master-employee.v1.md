# Data contract — Master Employee v1 (GOV-003)

| Item | Value |
|---|---|
| SoR | HR.Core `Employee` |
| Contract | master-employee.v1 |
| Publish events | `IEmployeeCreated`, `IEmployeeStatusChanged` |

## Logical fields (writers)

| Field | Type | Writer | Notes |
|---|---|---|---|
| Id | uuid | HR.Core | Stable employee key |
| TenantId | uuid | HR.Core | TEN-002 |
| NationalId | string≤20 | HR.Core | Unique per tenant |
| FullName / names | string | HR.Core | |
| CurrentStatus | enum | HR.Core | |
| PrimaryOrgUnitId | uuid? | HR.Core | |
| HireDate | date? | HR.Core | |

## Forbidden writers

- **Payroll** must not persist NationalId/FullName as SoR (lookup only).
- **Auth.Directory** must not overwrite HR identity fields; may store Username/Email as directory SoR.
- Consumers update local FKs/`EmployeeId` only.

## Freshness

Snapshots (pay profile OrgUnitId, analytics) may lag one event cycle; reconcile via DQ report when orphan/stale thresholds breached.
