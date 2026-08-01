# PROD-006 — Independent review waiver (production semantic-layer binding)

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | PROD-006 |
| Date | 2026-07-30 |
| Expires | When PROD-006a–e live stories are Accepted, or 2027-01-31 — whichever first |

## Waiver statement

Live warehouse/lakehouse (PROD-006a), live semantic metrics engine (PROD-006b), live BI dashboard / `LiveDashboardClaim` (PROD-006c), ANA-001/REP-001 join (PROD-006d), and open-data feed / PROD-007 (PROD-006e) are **deferred**. Engineering adopts lab semantic-layer contracts as a startup/CI binding. This waiver does **not** authorize live warehouse claims, live BI dashboards, open-data live feeds, or closing the WorkforcePlanning ANA-001 gap.

## Conditions

1. Matrix soft blockers remain honest: live-warehouse / live-bi-dashboard / live-open-data-feed=`Blocked`; ANA-001/REP-001=`NotStarted`; PROD-007=`Satisfied` (platform binding; open-data live remains Blocked).
2. `ProdSemantic:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveSemanticLayerClaim=false`.
4. STR-007 `LiveDashboardClaim` remains forbidden until PROD-006c / live-bi-dashboard.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Data & Analytics | Matrix + fixtures acceptable for interim |
| Privacy / Governance | Thresholds and RLS flags accepted |
| Product | Live BI remains PROD-006c |
