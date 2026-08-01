# PROD-006 UAT pack

**Status:** ReadyForSign-off

| Id | Check | Expect |
|---|---|---|
| U1 | ADR + blockers | GOV/FUN/PERF/TEN Satisfied; live-* Blocked; ANA NotStarted |
| U2 | KPI definition | kpi-definition-pass |
| U3 | Golden reconcile | golden-report-pass |
| U4 | Tenant RLS | tenant-scoped-pass |
| U5 | Privacy threshold | privacy-threshold-pass |
| U6 | Production binding | LiveSemanticLayerClaim=false; EnforceOnStartup Staging/Production |
| U7 | STR-007 retarget | LiveDashboardClaim forbidden until PROD-006c / live-bi-dashboard |
