# DPIA — Payroll processing (PRV-001)

| Item | Detail |
|---|---|
| Activity | `pa-payroll-compensation` |
| Risk | Financial + identity linkage; fraud/fraudulent payments; bulk export |
| Necessity | Required for public-sector compensation |
| Measures | SoD (IAM-003), immutable config (PAY-002), tenant RLS, audit/evidence, export job auth; since PLAN-53: a base salary, a pay grade, a salary scale or a grade's bounds changes only by a request with a reason and a legal reference that a second person approves (`PayProfileChangeRequest`, `PayrollReferenceChangeRequest`; tenant RLS on both tables; branch scope on the salary requests); the amounts are redacted in the audit rows (`[AuditRedact]`) and no amount, reason or legal reference is written to a log; the «submitted» event carries no amount; since PLAN-55: **the employee's bank account number** lives in `EmployeeBankAccounts.AccountNumber` (and, copied when an order is made, in `PaymentOrders.BeneficiaryIban`), under tenant RLS; it is registered by one person and verified by another; it is shown whole only on the approver's verification page, to a person who can verify it, and that page is never cached; every list, API answer and other page shows its last four characters; it is redacted in the audit rows with the holder's name (`[AuditRedact]`) and written to no log line, error message, hold reason, security audit event or URL; the DSAR answer says only «present» and «verified». **Not yet:** encryption of the number at rest (wave 2, SOV), and the employee registering his own account (wave 2) |
| Residual risk | Medium — accepted with controls |
| Approval | DPO + Payroll owner (record in STR-005 decision log before live payroll) |
