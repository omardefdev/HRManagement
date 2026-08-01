# EXT-007 — Independent live adapters / notify / PROD-005 review waiver

| Field | Value |
|---|---|
| Status | Active |
| Card | EXT-007 |
| Expires | When live-sector-adapters and EXT-007a–e are Accepted, or 2027-01-31 — whichever first |

## Scope

Live sector adapters, live notification channel, live adapter cert fleet, PROD-005 join, and COM-001 join are **deferred**. Engineering adopts lab sector-catalog contracts as a startup/CI binding. This waiver does **not** authorize live adapter claims, implicit critical integrations, sensitive PII in notifications, or swallowed failures.

## Conditions

1. Soft blockers remain honest in `sector-catalog-matrix.json`.
2. `ExtSector:EnforceOnStartup=true` in Staging/Production.
3. `LiveSectorCatalogClaim=false`.
4. EXT-005 matrix keeps historical outOfScope note for sector catalog product.
