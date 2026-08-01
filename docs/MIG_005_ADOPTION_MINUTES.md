# MIG-005 — Program adoption minutes (evidence production binding)

| Field | Value |
|---|---|
| Card | MIG-005 (card 70) |
| Date | 2026-07-30 |
| Decision | Bind recon-matrix into host startup and CI |
| Default live recon | `LiveAgencyReconClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/mig-evidence/recon-matrix.json` (copied to `MigEvidence/`) |
| Negative fixtures | unexplained-gap-fail, financial-variance-fail, dual-control-missing-fail, tamper-evidence-fail |
| Positive fixtures | full-accounted-pass, checksum-recon-pass, business-sample-signoff, statistical-sample-pass, evidence-package-stub, agency-wave-signoff |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveAgencyReconClaim=true` | MIG-005a–d Accepted |
| Live agency reconciliation | live-agency-recon + agency-extract-access Satisfied |
| Independent live SQL | live-independent-sql Satisfied (+ MIG-005d) |
| Production evidence vault | EVD-003 / MIG-005b |

Until then: lab harness only; live recon claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Migration QA | _pending wet-ink_ | |
| Internal Audit | _pending wet-ink_ | |
| Migration Office | _pending wet-ink_ | |

## Independent review

See [MIG_005_INDEPENDENT_REVIEW_WAIVER.md](MIG_005_INDEPENDENT_REVIEW_WAIVER.md).
