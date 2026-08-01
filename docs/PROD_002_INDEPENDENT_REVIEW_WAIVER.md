# PROD-002 — Independent live self-admin / ONB review waiver

| Field | Value |
|---|---|
| Status | Active |
| Card | PROD-002 |
| Expires | When live-agency-selfadmin and PROD-002a–e are Accepted, or 2027-01-31 — whichever first |

## Scope

Live agency self-admin UI, live delegated admin product, live quotas/calendars/notifications admin, ONB-001 join, and PROD-003 join are **deferred**. Engineering adopts lab agency-selfadmin contracts as a startup/CI binding. This waiver does **not** authorize live self-admin claims, national control mutation, cross-tenant admin, or sensitive changes without four-eyes.

## Conditions

1. Soft blockers remain honest in `agency-selfadmin-matrix.json`.
2. `ProdAgency:EnforceOnStartup=true` in Staging/Production.
3. `LiveAgencySelfAdminClaim=false`.
4. MIG-007 historical outOfScope note for PROD-002 self-admin remains.
