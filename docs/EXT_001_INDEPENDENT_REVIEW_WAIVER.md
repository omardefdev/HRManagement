# EXT-001 — Independent national/agency IdP / phishing-MFA / conformance review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | EXT-001 |
| Expires | When national-idp-live, agency-idp-live, phishing-mfa-fleet, live-federation-conformance, and EXT-001a–d are Accepted, or 2027-01-31 — whichever first |
| Owner | National IAM + Security Architecture |

## Waiver statement

Live national IdP bind, agency IdP broker, phishing-resistant MFA fleet, live federation conformance, and federation workers (EXT-001a–d) are **deferred**. Engineering adopts the assurance matrix as a startup/CI binding with lab harness fixtures. This waiver does **not** authorize:

- Claiming live federation while `LiveFederationClaim` must stay false
- Marking national/agency IdP stubs as `live=true` in this binding
- Name-only account linking or SMS OTP for sensitive roles
- Revocation SLA breach without escalation
- Shared citizen/employee principals

## Conditions

1. Matrix blockers remain: IAM-003/IAM-004/STR-002=`Satisfied`; IAM-001 / national-idp-live / agency-idp-live / phishing-mfa-fleet / live-federation-conformance=`Blocked`.
2. `ExtFederation:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveFederationClaim=false`.
4. Negative fixtures (name-only link / revocation SLA breach) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| National IAM | Lab federation binding acceptable for interim |
| Security Architecture | Persona×AAL + noNameOnlyLinking accepted as gate |
| Migration Office | Live IdP deferred to EXT-001a–d |
| Privacy | Opaque identifiers + forbid plain national ID in logs accepted |
