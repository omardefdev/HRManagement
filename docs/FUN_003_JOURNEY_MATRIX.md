# FUN-003 — مصفوفة رحلات النقل والانتداب والإعارة

المصدر: [`fun-movements/journey-matrix.json`](./fun-movements/journey-matrix.json) · ADR: [`ADR-FUN-003-movements-journey-closure.md`](./ADR-FUN-003-movements-journey-closure.md)

## Transfer

| خطوة | إلزامي | مالك | SLA | خدمة |
|---|---|---|---:|---|
| TransferRequest | نعم | hr-movements | 24 | HR.Movements |
| TransferApproved | نعم | hr-management-approver | 48 | HR.Movements |
| FinancialImpact | نعم | payroll | 48 | Payroll |
| OrgSync | نعم | hr-core | 24 | HR.Core |
| DirectoryScope | نعم | auth-directory | 24 | Auth.Directory |
| AssetsMove | لا | admin-logistics | 72 | Admin.Logistics |
| TransferExecuted | نعم | hr-movements | 1 | TransferSaga |

**أطراف saga:** Payroll, HR.Core, Auth.Directory, Admin.Logistics.

## Secondment (انتداب)

| خطوة | إلزامي | مالك | SLA | خدمة |
|---|---|---|---:|---|
| SecondmentRequest / Approved | نعم | hr-movements / approver | 24–48 | HR.Movements |
| PayImpact / DirectoryHost | نعم | payroll / auth | 24–48 | saga parties |
| SecondmentActivated / Ended | نعم | hr-movements | 1–24 | saga + Close |

## Loan (إعارة) — event-hardened

Checklist + SoD + صلاحية مصدر/هدف؛ Activate → `ILoanAssignmentExecuted`؛ Close → `ILoanAssignmentEnded` (لا Automatonymous في FUN-003).

**قاعدة:** لا Execute/Activate قبل اكتمال الخطوات الإلزامية بدليل؛ صلاحية AND على المصدر والهدف.
