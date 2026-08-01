# STR-006 — Program succession

Program role succession — **not** the HRCore `SuccessionPlan` employee feature.

| roleId | successorRoles | KT plan |
|---|---|---|
| payroll-lead | payroll-backup | KT#payroll-lead |
| platform-lead | platform-backup, sre-a | KT#platform-lead |
| dba-primary | dba-backup | KT#dba-primary |
| sponsor | program | KT#sponsor |

Source: `succession[]` in ownership-matrix. Validate requires at least one successor for each listed critical primary role covered by succession rows.
