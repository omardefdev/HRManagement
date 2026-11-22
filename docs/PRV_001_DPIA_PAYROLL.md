# DPIA — Payroll processing (PRV-001)

| Item | Detail |
|---|---|
| Activity | `pa-payroll-compensation` |
| Risk | Financial + identity linkage; fraud/fraudulent payments; bulk export |
| Necessity | Required for public-sector compensation |
| Measures | SoD (IAM-003), immutable config (PAY-002), tenant RLS, audit/evidence, export job auth; since PLAN-53: a base salary, a pay grade, a salary scale or a grade's bounds changes only by a request with a reason and a legal reference that a second person approves (`PayProfileChangeRequest`, `PayrollReferenceChangeRequest`; tenant RLS on both tables; branch scope on the salary requests); the amounts are redacted in the audit rows (`[AuditRedact]`) and no amount, reason or legal reference is written to a log; the «submitted» event carries no amount |
| Residual risk | Medium — accepted with controls |
| Approval | DPO + Payroll owner (record in STR-005 decision log before live payroll) |
