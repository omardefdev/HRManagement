# STR-006 — Ownership matrix (human view)

Machine source: [`str-ownership/ownership-matrix.json`](str-ownership/ownership-matrix.json).

Rule: every **critical** component has product/service/data/security owners, `busFactor >= 2`, and at least one backup role.

| id | tier | critical | product | service | data | security | primary | backups | busFactor |
|---|---|---|---|---|---|---|---|---|---|
| payroll | 0 | Yes | product-payroll | payroll-ops | data-payroll | security | payroll-lead | payroll-backup, platform-lead | 3 |
| gateway | 0 | Yes | product-platform | platform | data-platform | security | platform-lead | platform-backup, sre-a | 3 |
| hr-core | 0 | Yes | product-hr | platform | data-hr | security | hr-core-lead | hr-core-backup, platform-lead | 3 |
| auth-directory | 0 | Yes | product-platform | platform | data-identity | security | auth-lead | auth-backup, security | 3 |
| postgres | 0 | Yes | product-platform | dba | data-platform | security | dba-primary | dba-backup, platform-lead | 3 |
| compliance | 1 | Yes | product-compliance | platform | data-compliance | security | compliance-lead | compliance-backup | 2 |
| dev-analytics | 2 | No | product-analytics | platform | data-analytics | security | analytics-lead | analytics-backup | 2 |

Joins: REL-003 `services[].id` and STR-003 `modules[].moduleId` where listed. Distinct from STR-005 decision boards and GOV-003 field ownership.
