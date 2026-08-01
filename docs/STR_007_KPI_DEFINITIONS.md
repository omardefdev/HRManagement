# STR-007 — KPI definitions (human view)

Machine source: [`str-value/kpi-matrix.json`](str-value/kpi-matrix.json).

Rule: every KPI has definition, source, owner ≠ auditor, target, anti-gaming, and baseline freeze when `baselineRequired`.

| id | unit | owner | auditor | target | modules |
|---|---|---|---|---|---|
| hire_cycle_time | days | product-hr | audit | ↓ 45 | hr-core, hr-contracts |
| separation_cycle_time | days | product-hr | audit | ↓ 10 | hr-movements, auth-directory |
| payroll_cycle_time | hours | product-payroll | audit | ↓ 48 | payroll |
| self_service_rate | % | product | audit | ↑ 40 | hr-core (PROD-003 stub) |
| data_error_duplicate_rate | /1000 | data | audit | ↓ 5 | hr-core (GOV-003) |
| user_satisfaction | score | product | audit | ↑ 4.0 | hr-core, payroll |
| uptime_value | % | sre | audit | ↑ 99.5 | OBS J1 |
| cost_per_employee | currency | finance | audit | TBD FIN-001 | — |
| agency_onboarding_time | days | program | audit | ↓ 90 | payroll, hr-core |
| adoption_rate | % | product | audit | ↑ 70 | hr-core, payroll, hr-contracts |

Forbidden as benefit measures: `screenCount`, `pageCount`, `widgetCount`.
