# PROD-007 — Independent review waiver (production extensibility-platform binding)

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | PROD-007 |
| Date | 2026-07-30 |
| Expires | When PROD-007a–e live stories are Accepted, or 2027-01-31 — whichever first |

## Waiver statement

Live extension/plugin runtime (PROD-007a), live partner SDK (PROD-007b), live webhook delivery (PROD-007c), EXT-006 certification surface (PROD-007d), and REL-004 / upgrade ops (PROD-007e) are **deferred**. Engineering adopts lab extensibility contracts as a startup/CI binding. This waiver does **not** authorize live plugin hosts, live partner SDKs, live webhooks, or closing EXT-006 / IOP-001 / REL-004.

## Conditions

1. Matrix soft blockers remain honest: live-extension-runtime / live-partner-sdk / live-webhook-delivery=`Blocked`; EXT-006/IOP-001/REL-004=`NotStarted`.
2. `ProdExtensibility:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveExtensibilityClaim=false`.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Product Architecture | Matrix + fixtures acceptable for interim |
| Integration Governance | N-2 / isolation / no-direct-DB flags accepted |
| Product | Live runtime remains PROD-007a |
