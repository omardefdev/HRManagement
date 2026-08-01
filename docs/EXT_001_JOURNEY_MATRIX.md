# EXT-001 — Journey matrix

Source: [`ext-federation/assurance-matrix.json`](ext-federation/assurance-matrix.json) — [ADR-EXT-001](ADR-EXT-001-gov-identity-federation-assurance.md).

## AssuranceMapping

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| LevelsDefined | Yes | national-iam | assuranceLevels + personas |
| SignedMatrix | Yes | security-architecture | assurance-matrix-signoff |

## FederationInterop

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| TrustInventory | Yes | national-iam | federationTrust |
| NationalInterop | Yes | qa | national-idp-interop-pass |
| AgencyInterop | Yes | qa | agency-idp-interop-pass |

## RevocationSla

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| SlaPolicy | Yes | national-iam | revocation |
| SlaPass | Yes | qa | revocation-sla-pass |
| SlaBreachFail | Yes | qa | revocation-sla-breach-fail |

## NoNameOnlyLink

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AttrPolicy | Yes | privacy | attributeAuthority |
| NameOnlyFail | Yes | qa | name-only-link-fail |

## CredentialLoss

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| LossPlaybook | Yes | national-iam | lifecycle |
| LossPass | Yes | qa | credential-loss-pass |

## IdpOutageFailover

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| OutagePass | Yes | qa | idp-outage-failover-pass |

## FraudCases

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| FraudHarness | Yes | qa | fraud-cases |

Soft blockers: IAM-001, national-idp-live, agency-idp-live, phishing-mfa-fleet, live-federation-conformance = Blocked.
