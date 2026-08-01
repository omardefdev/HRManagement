# MIG-001 — Independent agency-extract / SYR-002 / MIG-003 review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | MIG-001 |
| Expires | When agency-extract-access, SYR-002, and MIG-003 are Accepted, or 2027-01-31 — whichever first |
| Owner | Migration Office + Data Architecture |

## Waiver statement

Live agency read paths (agency-extract-access), official census volumes (SYR-002), and the migration ETL factory (MIG-003) are **deferred**. Engineering adopts the source inventory as a startup/CI binding with lab measured counts and signed unsupported exceptions. This waiver does **not** authorize:

- Claiming live agency extract while `LiveAgencyExtractClaim` must stay false
- Shipping Inventoried sources with estimateOnly volumes
- Critical target fields without SoT / gov003Join
- Empty accessRisks on Inventoried sources

## Conditions

1. Matrix blockers remain: STR-001/GOV-003=`Satisfied`, agency-extract-access=`Blocked`, SYR-002=`Blocked`.
2. `MigInventory:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveAgencyExtractClaim=false`.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Migration Office | Lab inventory binding acceptable for interim |
| Data Architecture | Field SoT stubs accepted pending live extract |
| Security | Access-risk documentation accepted as gate |
