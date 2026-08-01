# MIG-007 — Program adoption minutes (onboarding production binding)

| Field | Value |
|---|---|
| Card | MIG-007 (card 72) |
| Date | 2026-07-30 |
| Decision | Bind onboarding-matrix into host startup and CI |
| Default live onboard | `LiveDualAgencyOnboardClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/mig-onboarding/onboarding-matrix.json` (copied to `MigOnboarding/`) |
| Negative fixtures | fork-attempt-fail, wave-without-signoff-fail |
| Positive fixtures | readiness-checklist-pass, agency-a/b-e2e, lead-time-pass, defects-telemetry, exit-rollback-pass, onboarding-run-ledger |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveDualAgencyOnboardClaim=true` | MIG-007a Accepted + live-dual-agency-onboard Satisfied |
| Live sandbox/portal | MIG-007b + EXT-006 |
| Self-admin | MIG-007c + PROD-002 |
| Provision / four-eyes | MIG-007d + ONB-001 |

Until then: lab harness only; live onboard claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Onboarding Office | _pending wet-ink_ | |
| Internal Audit | _pending wet-ink_ | |
| Migration Office | _pending wet-ink_ | |

## Independent review

See [MIG_007_INDEPENDENT_REVIEW_WAIVER.md](MIG_007_INDEPENDENT_REVIEW_WAIVER.md).
