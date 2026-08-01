# EXT-001 — Program adoption minutes (federation production binding)

| Field | Value |
|---|---|
| Card | EXT-001 (card 73) |
| Date | 2026-07-30 |
| Decision | Bind assurance-matrix into host startup and CI |
| Default live federation | `LiveFederationClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/ext-federation/assurance-matrix.json` (copied to `ExtFederation/`) |
| Negative fixtures | name-only-link-fail, revocation-sla-breach-fail |
| Positive fixtures | assurance-matrix-signoff, national/agency interop, revocation-sla-pass, credential-loss-pass, idp-outage-failover-pass, fraud-cases, federation-run-ledger |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveFederationClaim=true` | EXT-001a–d Accepted + live-federation-conformance Satisfied |
| National IdP live | EXT-001a + national-idp-live Satisfied |
| Agency IdP live | EXT-001b + agency-idp-live Satisfied |
| Phishing-MFA fleet | EXT-001c + phishing-mfa-fleet Satisfied |

Until then: lab harness only; live federation claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| National IAM | _pending wet-ink_ | |
| Security Architecture | _pending wet-ink_ | |
| Migration Office | _pending wet-ink_ | |

## Independent review

See [EXT_001_INDEPENDENT_REVIEW_WAIVER.md](EXT_001_INDEPENDENT_REVIEW_WAIVER.md).
