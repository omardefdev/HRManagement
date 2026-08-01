# STR-007 — Independent FIN-001 / PROD-003 / PROD-006c review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | STR-007 |
| Expires | When FIN-001, PROD-003, and PROD-006c (live-bi-dashboard) are Accepted, or 2027-01-31 — whichever first |
| Owner | Product + Audit + Finance |

## Waiver statement

Independent finance SoT for `cost_per_employee` (FIN-001), live ESS portals for self-service rate (PROD-003), and live BI/value dashboards (PROD-006c / live-bi-dashboard — PROD-006 production semantic-layer binding Done does not enable live BI) are **deferred**. Engineering adopts the KPI matrix as a startup/CI binding with frozen baselines and privacy rules. This waiver does **not** authorize:

- Claiming live national value dashboards while `LiveDashboardClaim` must stay false
- Measuring benefits via screen/page/widget counts
- Shipping KPIs with owner==auditor or without anti-gaming / pre-pilot freeze
- Cross-tenant value rollups without waiver

## Conditions

1. Matrix blockers remain: STR-001/003/GOV-003=`Satisfied`, FIN-001=`NotStarted`, PROD-003=`Blocked`.
2. `ProgramValue:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveDashboardClaim=false`.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Product | Catalog + baselines acceptable for interim |
| Audit | Independent measurement fixtures accepted |
| Finance | cost_per_employee stub accepted pending FIN-001 |
