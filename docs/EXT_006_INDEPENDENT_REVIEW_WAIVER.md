# EXT-006 — Independent live portal / partner fleet review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | EXT-006 |
| Expires | When live-developer-portal and EXT-006a–e are Accepted, or 2027-01-31 — whichever first |
| Owner | API Product |

## Waiver statement

Live developer portal, partner cert fleet, production key rotation, live analytics, and MIG-007 EXT-006 Satisfied flip are **deferred**. Engineering adopts lab sandbox-cert contracts as a startup/CI binding. This waiver does **not** authorize live portal claims, real PII in sandbox, manual secrets, or certifying incompatible clients.

## Conditions

1. Blockers remain: EXT-005/REL-001=`Satisfied`; live-developer-portal / real-pii-in-sandbox / partner-cert-fleet-live=`Blocked`; EXT-007=`NotStarted`.
2. `ExtSandbox:EnforceOnStartup=true` in Staging/Production.
3. Default `LiveSandboxPortalClaim=false`.
4. MIG-007 matrix EXT-006 stays NotStarted; EXT-005 partner-cdc-cert stays Blocked.
