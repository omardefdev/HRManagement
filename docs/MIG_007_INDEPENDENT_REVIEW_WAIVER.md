# MIG-007 — Independent EXT-006 / PROD-002 / ONB-001 / live dual-agency review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | MIG-007 |
| Expires | When EXT-006, PROD-002, ONB-001, live-dual-agency-onboard, and MIG-007a–d are Accepted, or 2027-01-31 — whichever first |
| Owner | Onboarding Office + Internal Audit |

## Waiver statement

Developer sandbox/portal (EXT-006), agency self-admin (PROD-002), signed provision/four-eyes (ONB-001), live dual-agency onboard, and onboarding workers (MIG-007a–d) are **deferred**. Engineering adopts the onboarding matrix as a startup/CI binding with lab harness fixtures. This waiver does **not** authorize:

- Claiming live dual-agency onboard while `LiveDualAgencyOnboardClaim` must stay false
- Per-agency product forks when `noforkRequired` is true
- Starting a wave without complete sign-offs
- Live portal while `livePortalForbidden` is true
- Missing data / security / ops owners

## Conditions

1. Matrix blockers remain: MIG-006/STR-004=`Satisfied`; EXT-006/PROD-002=`NotStarted`; ONB-001 / agency-extract-access / live-dual-agency-onboard=`Blocked`.
2. `MigOnboarding:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveDualAgencyOnboardClaim=false`.
4. Negative fixtures (fork / wave-without-signoff) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Onboarding Office | Lab onboarding binding acceptable for interim |
| Internal Audit | Nofork + wave sign-offs accepted as gate |
| Migration Office | Live dual-agency deferred to MIG-007a–d |
| Product / STR | STR-004 nofork join accepted |
