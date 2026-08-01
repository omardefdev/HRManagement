# SoD Matrix — فصل الواجبات (IAM-003)

## Maker / Checker / Poster / Reverser

| EntityType | Maker | Checker | Poster | Reverser | Different actors | Financial threshold | Min approvals |
|---|---|---|---|---|---|---|---|
| PayrollRun | payroll-processor | payroll-approver | payroll-poster | payroll-poster / payroll-reverser | yes | 50,000,000 SYP | 1 (2 above threshold) |
| LeaveRequest | hr-contracts | hr-contracts | — | — | yes | — | 1 |
| AdvanceRequest | hr-contracts | hr-contracts | — | — | yes | 5,000,000 SYP | 1 (2 above threshold) |
| TransferRequest | hr-movements | hr-management-approver | — | — | yes | — | 1 |
| ApprovalRequest | hr-core | hr-management-approver | — | — | yes | — | 1 |
| RoleAssignment | auth-directory | hr-admin | — | — | yes | — | 2 (four-eyes) |
| LegalDataChange | hr-core | hr-management-approver | — | — | yes | — | 2 (four-eyes) |
| EosSettlement | payroll-approver | payroll-poster | — | — | yes | 50,000,000 SYP | 1 |

## Conflicting role pairs

| Role A | Role B | Reason |
|---|---|---|
| payroll-processor | payroll-approver | لا يجوز الجمع بين إنشاء التشغيل واعتماده |
| payroll-approver | payroll-poster | لا يجوز الجمع بين الاعتماد والترحيل |
| payroll-processor | payroll-poster | لا يجوز الجمع بين الإنشاء والترحيل |
| hr-core | hr-management-approver | الكاتب لا يعتمد قرارات الإدارة العليا لنفس الكيان |

## Trusted identity

- Approval actors are taken **only** from the authenticated token (`NameIdentifier` / `sub`).
- Client-supplied `approverId` form/query parameters were removed from Transfer, Leave, Advance, and EmploymentRecords approve/reject actions.

## Delegation

- Max duration: 90 days
- No self-delegation
- Scoped by optional `EntityTypeScope` / `RoleScope`
- Managed via `DelegationsController` (policy `SoD.ManageDelegations`)

## Reporting

Run periodic conflict detection:

```powershell
pwsh ./scripts/sod-report.ps1 -ConnectionString $env:HRCORE_CONNECTION -OutputPath ./artifacts/sod-report.json
```

## Privileged roles (four-eyes grant)

- `hr-admin`
- `platform-admin`
- `payroll-poster`

## Legal fields requiring dual approval

- NationalId, DateOfBirth / BirthDate, HireDate / EmploymentDate, FullName (First/Last)
