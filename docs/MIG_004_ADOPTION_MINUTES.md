# MIG-004 — Program adoption minutes (match production binding)

| Field | Value |
|---|---|
| Card | MIG-004 (card 69) |
| Date | 2026-07-30 |
| Decision | Bind match-matrix into host startup and CI |
| Default live matcher | `LiveIdentityMatchClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/mig-match/match-matrix.json` (copied to `MigMatch/`) |
| Negative fixtures | high-risk-auto-merge-fail, cross-tenant-review-fail |
| Positive fixtures | threshold-signoff, merge-unmerge-roundtrip, identity-pairs, precision-recall-lab, twins-name-variants, match-decision-ledger |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveIdentityMatchClaim=true` | MIG-004a–d Accepted |
| Live agency P/R evaluation | live-labelled-metrics + agency-extract-access Satisfied |
| Full Arabic normalization | L10N-002 Satisfied |
| Golden person / continuous fraud | MDM-002 / FRD-001 (out of this binding) |

Until then: lab labelled pairs only; live matcher claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Data Quality | _pending wet-ink_ | |
| Privacy Officer | _pending wet-ink_ | |
| Migration Office | _pending wet-ink_ | |

## Independent review

See [MIG_004_INDEPENDENT_REVIEW_WAIVER.md](MIG_004_INDEPENDENT_REVIEW_WAIVER.md).
