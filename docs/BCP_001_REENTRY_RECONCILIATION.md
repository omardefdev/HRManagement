# BCP-001 — Re-entry and reconciliation

**Card:** BCP-001  
**Purpose:** After manual/degraded operations, reconcile 100% of paper backlog into the system without duplication or data loss.

## Pre-re-entry gates

- [ ] IC declares end of manual mode
- [ ] Platform `/health/ready` green for Gateway, HR.Core, Payroll, Keycloak
- [ ] DR/failover complete if applicable (OPS-004)
- [ ] Re-entry team staffed (HR ops + 2 data entry + 1 auditor)

## Reconciliation order

1. **Identity** — Revoke temporary credentials; confirm Keycloak sessions match roster.
2. **Master data** — Org units and employee status (suspended flags from paper).
3. **Leave** — L-01/L-02 forms entered with `ManualEntrySource=BCP` and paper form ID.
4. **Attendance** — A-01 corrections batched by date.
5. **Payroll** — Compare emergency run sheet totals to system pay run; attach evidence package.
6. **Contracts** — Scan and index amendments; link to employee record.

## Duplicate detection rules

| Entity | Key | Action on duplicate |
|--------|-----|---------------------|
| Leave request | EmployeeId + StartDate + Type | Merge if same approver; else escalate |
| Payroll line | EmployeeId + PayPeriod + Component | Keep system record if amounts match; else IC review |
| Temp account | Username | Disable before merge |

## Evidence package

Each re-entry batch requires:

- Scanner operator ID
- Paper form ID range
- Timestamp (Asia/Damascus)
- Hash of scanned PDF bundle (SHA-256)

Store under `integration-outbox/bcp-reentry/{date}/`.

## Sign-off

| Role | Responsibility |
|------|----------------|
| HR Operations Lead | Data entry complete |
| Internal Audit | 10% sample match paper |
| IC | Close BCP incident |

## Success criteria

- 100% of numbered paper forms accounted for (used, void, or pending)
- Zero duplicate pay lines in target pay period
- Audit trail links every manual record to paper ID

## Validation

```powershell
powershell -File scripts/bcp001-validate.ps1
dotnet test --filter Bcp001ContinuityTests
```
