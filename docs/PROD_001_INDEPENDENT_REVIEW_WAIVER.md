# PROD-001 — Independent live engine / WF review waiver

| Field | Value |
|---|---|
| Status | Active |
| Card | PROD-001 |
| Expires | When live-policy-engine and PROD-001a–e are Accepted, or 2027-01-31 — whichever first |

## Scope

Live policy engine, live workflow runtime, live form designer, live agency override product, and WF-001/WF-002 / ApprovalStep rewrite are **deferred**. Engineering adopts lab policy-platform contracts as a startup/CI binding. This waiver does **not** authorize live engine claims, invalid publish, real-data simulation, arbitrary code, or overrides that bypass national mandatory controls.

## Conditions

1. Soft blockers remain honest in `policy-platform-matrix.json`.
2. `ProdPolicy:EnforceOnStartup=true` in Staging/Production.
3. `LivePolicyEngineClaim=false`.
4. STR-004 keeps ONB-001 Blocked; `prod001Dependency` surface flags remain.
