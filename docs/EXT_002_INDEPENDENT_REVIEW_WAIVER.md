# EXT-002 — Independent MOI / sandbox / L10N review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | EXT-002 |
| Expires | When moi-civil-registry-live, live-sandbox-cert, and EXT-002a–d are Accepted, or 2027-01-31 — whichever first |
| Owner | Integration + Privacy |

## Waiver statement

Live MOI bind, certified sandbox, production rate/audit sink, correction UI, and civil-registry workers (EXT-002a–d) are **deferred**. Engineering adopts the civil-registry matrix as a startup/CI binding with lab harness fixtures. This waiver does **not** authorize:

- Claiming live MOI while `LiveMoiClaim` must stay false
- Marking `liveMoiForbidden=false` in this binding
- Purpose outside allowlist, excess field storage, bulk harvest, or stale/outage-as-truth
- Queries without required audit fields

## Conditions

1. Matrix blockers remain: EXT-001/GOV-001/MIG-004=`Satisfied`; moi-civil-registry-live / live-sandbox-cert / national-idp-live=`Blocked`; L10N-002=`NotStarted`.
2. `ExtCivil:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveMoiClaim=false`.
4. Negative fixtures (purpose / excess / outage / bulk / stale) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Integration + Privacy | Lab civil-registry binding acceptable for interim |
| Data Protection Officer | Purpose + minimal + audit accepted as gate |
| Migration Office | Live MOI deferred to EXT-002a–d |
| Security | Bulk harvest forbidden accepted |
