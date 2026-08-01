# ADR-EXT-001 — Federation production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-001 (card 73) |
| Date | 2026-07-30 |
| Supersedes | Federation/assurance gate pack as insufficient for production |
| Dependencies | IAM-003, IAM-004, STR-002 Satisfied; IAM-001 Blocked |

## Context

The prior pack published the assurance matrix, persona×AAL mapping, federation trust stubs, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `ExtFederation` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/ext-federation/assurance-matrix.json` |
| Runtime | `ExtFederationOptions` + `IExtFederationProvider` + `IFederationAssuranceClaimGuard` |
| Assurance | AAL1–AAL3; personas with High/Critical ≥ AAL2; sensitive roles AAL3 |
| Federation | national + agency lab stubs; `live=false` |
| MFA | phishing-resistant roles; SMS OTP forbidden for sensitive |
| Revocation | SLA minutes; breach without escalation forbidden |
| Separation | citizen/employee; forbid shared principal |
| Attributes | noNameOnlyLinking; forbid FullName |
| Live federation | Default `LiveFederationClaim=false`; forbidden until EXT-001a–d |
| Soft blockers | IAM-003/004/STR-002 Satisfied; IAM-001 / national-idp-live / agency-idp-live / phishing-mfa-fleet / live-federation-conformance Blocked |
| Closure | **Done (production federation binding)** |

## Consequences

- Staging/Production fail startup on incomplete assurance policy, live IdP stubs, dishonest blockers, or live federation claim.
- CI runs `ext001-federation-runtime-validate.ps1`.
- Live national/agency IdP, WebAuthn fleet, and live conformance remain out of scope.
