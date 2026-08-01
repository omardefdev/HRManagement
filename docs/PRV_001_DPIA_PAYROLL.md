# DPIA — Payroll processing (PRV-001)

| Item | Detail |
|---|---|
| Activity | `pa-payroll-compensation` |
| Risk | Financial + identity linkage; fraud/fraudulent payments; bulk export |
| Necessity | Required for public-sector compensation |
| Measures | SoD (IAM-003), immutable config (PAY-002), tenant RLS, audit/evidence, export job auth |
| Residual risk | Medium — accepted with controls |
| Approval | DPO + Payroll owner (record in STR-005 decision log before live payroll) |
