# EXT-005 — Independent bus / registry / EXT-006 review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | EXT-005 |
| Expires | When gov-exchange-bus-live, schema-registry-live, and EXT-005a–e are Accepted, or 2027-01-31 — whichever first |
| Owner | API Governance |

## Waiver statement

Live government exchange bus, production schema registry, PROD-007 join, EXT-006 certification surface, IOP/SYR profiles, and api-exchange workers (EXT-005a–e) are **deferred**. Engineering adopts the api-exchange matrix as a startup/CI binding with lab harness fixtures. This waiver does **not** authorize:

- Claiming live exchange while `LiveExchangeClaim` must stay false
- Marking `liveBusForbidden=false` or `schemaRegistry.liveForbidden=false` in this binding
- Shared-DB integrations, breaking without major, certified-consumer breaks, purpose-denied exports, or deprecation below noticeDaysMin

## Conditions

1. Matrix blockers remain: IAM-004/TEN-003/INT-002=`Satisfied`; PROD-007=`Satisfied` (production extensibility-platform binding); gov-exchange-bus-live / schema-registry-live / partner-cdc-cert / national-openapi-publish=`Blocked`.
2. `ExtApi:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveExchangeClaim=false`.
4. Negative fixtures (owner/SLA / breaking / shared-db / CDC / purpose / schema / deprecation) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| API Governance | Lab api-exchange binding acceptable for interim |
| Integration Architecture | Catalog + compat + consumer-driven accepted as gate |
| Privacy | Purpose-bound export accepted as gate |
| Product | PROD-007 / EXT-006 deferred |
