# MIG-005 — Financial controls

| Rule | Value |
|---|---|
| payrollControlRequired | true |
| controlTotalTolerance | 0 (lab) |
| unexplainedVarianceForbidden | true |

Payroll/control totals from source vs platform must match within tolerance. A non-zero variance without an attached explained-adjustment reason fails validate (`financial-variance-fail`).

Live agency payroll recon remains **Blocked** (`live-agency-recon`).
