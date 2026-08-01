# MIG-003 — Independent agency-extract / live-cutover-perf / ETL workers review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | MIG-003 |
| Expires | When agency-extract-access, live-cutover-perf, and MIG-003a–d are Accepted, or 2027-01-31 — whichever first |
| Owner | Migration Engineering + Migration Office |

## Waiver statement

Live agency extract (agency-extract-access), live cutover performance proof (live-cutover-perf), and production ETL workers (MIG-003a–d) are **deferred**. Engineering adopts the pipeline matrix as a startup/CI binding with lab harness fixtures. This waiver does **not** authorize:

- Claiming live ETL workers while `LiveEtlWorkersClaim` must stay false
- Unencrypted or non-tenant-scoped staging zones
- Non-idempotent reload (`skip-identical` / `duplicateOnReloadForbidden` required)
- Missing lineage fields or PII in log samples
- `liveProofRequired=true` or fixture `liveProof=true` while live-cutover-perf is Blocked

## Conditions

1. Matrix blockers remain: MIG-002/TEN-002/OPS-005=`Satisfied`, agency-extract-access=`Blocked`, live-cutover-perf=`Blocked`.
2. `MigFactory:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveEtlWorkersClaim=false`.
4. Negative fixture `corrupt-schema-fail` remains in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Migration Engineering | Lab factory binding acceptable for interim |
| Security | Staging encryption + PII log policy accepted as gate |
| Migration Office | ETL workers deferred to MIG-003a–d |
| Performance | Live cutover proof deferred with live-cutover-perf |
