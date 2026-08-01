# MIG-005 — Independent agency-extract / live-agency-recon / live-independent-sql / vault review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | MIG-005 |
| Expires | When agency-extract-access, live-agency-recon, live-independent-sql, and MIG-005a–d are Accepted, or 2027-01-31 — whichever first |
| Owner | Migration QA + Internal Audit |

## Waiver statement

Live agency extract/recon (agency-extract-access / live-agency-recon), independent live SQL (live-independent-sql), production evidence vault (EVD-003 / MIG-005b), and recon workers (MIG-005a–d) are **deferred**. Engineering adopts the recon matrix as a startup/CI binding with lab harness fixtures. This waiver does **not** authorize:

- Claiming live agency recon while `LiveAgencyReconClaim` must stay false
- Unexplained accounting gaps or financial variance above tolerance 0
- Evidence packages that are not immutable / hash-verified
- Dual control with fewer than two distinct roles
- `liveIndependentSql=true` or `liveAgencyExtract=true` while related blockers are Blocked

## Conditions

1. Matrix blockers remain: MIG-003/OBS-003=`Satisfied`, agency-extract-access=`Blocked`, live-agency-recon=`Blocked`, live-independent-sql=`Blocked`.
2. `MigEvidence:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveAgencyReconClaim=false`.
4. Negative fixtures (gap / variance / dual-control / tamper) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Migration QA | Lab evidence binding acceptable for interim |
| Internal Audit | Dual control + immutable package accepted as gate |
| Migration Office | Live recon deferred to MIG-005a–d |
| Security / OBS | OBS-003 WORM join accepted pending EVD-003 vault |
