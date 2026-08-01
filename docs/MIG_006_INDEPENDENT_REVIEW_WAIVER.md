# MIG-006 — Independent agency-extract / live-cutover-perf / live-rehearsal / DNS review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | MIG-006 |
| Expires | When agency-extract-access, live-cutover-perf, live-agency-recon, live-rehearsal-at-scale, and MIG-006a–d are Accepted, or 2027-01-31 — whichever first |
| Owner | Cutover Command + Internal Audit |

## Waiver statement

Live agency extract (agency-extract-access), live cutover performance (live-cutover-perf), live post-cutover recon (live-agency-recon), agency-scale rehearsal (live-rehearsal-at-scale), production DNS, and cutover workers (MIG-006a–d) are **deferred**. Engineering adopts the cutover matrix as a startup/CI binding with lab harness fixtures. This waiver does **not** authorize:

- Claiming live agency cutover while `LiveAgencyCutoverClaim` must stay false
- Paper-only rollback or rehearsals that are not timed / within window / volume OK
- Delta capture with loss when `noLossRequired` is true
- Live DNS flip while `liveForbiddenInThisPack` is true
- Go decision without mandatory criteria met

## Conditions

1. Matrix blockers remain: MIG-005/OPS-003/REL-001=`Satisfied`; agency-extract-access / live-cutover-perf / live-agency-recon / live-rehearsal-at-scale=`Blocked`.
2. `MigCutover:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveAgencyCutoverClaim=false`.
4. Negative fixtures (delta-loss / paper-rollback) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Cutover Command | Lab cutover binding acceptable for interim |
| Internal Audit | Exercised rollback + Go/No-Go accepted as gate |
| Migration Office | Live cutover deferred to MIG-006a–d |
| Ops / REL | OPS-003 + REL-001 joins accepted pending live window |
