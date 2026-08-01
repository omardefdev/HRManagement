# EXT-003 — Program adoption minutes (treasury-payment production binding)

| Field | Value |
|---|---|
| Card | EXT-003 (card 75) |
| Date | 2026-07-30 |
| Decision | Bind treasury-payment-matrix into host startup and CI |
| Default live treasury | `LiveTreasuryClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/ext-treasury/treasury-payment-matrix.json` (copied to `ExtTreasury/`) |
| Negative fixtures | unapproved-run-fail, duplicate-submit-fail, totals-mismatch-fail, manual-file-unaudited-fail, cutoff-breach-fail |
| Positive fixtures | recon-totals-pass, partial-reject-correct-pass, out-of-order-ack-pass, delay-ack-pass, parallel-payroll-pass, payment-order-audit-pass, treasury-run-ledger |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveTreasuryClaim=true` | EXT-003a Accepted + treasury-live Satisfied |
| Live bank / postal channel | EXT-003b + bank-sandbox-cert / postal-channel-live Satisfied |
| ProcessPaymentBatch + ack consumer | EXT-003c |
| SYR-011 / PAY-004 | EXT-003d |
| Live PKI | EXT-003e + EXT-004 |

Until then: lab harness only; live treasury claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Payroll + Finance Integration | _pending wet-ink_ | |
| Finance Control | _pending wet-ink_ | |
| Integration Office | _pending wet-ink_ | |

## Independent review

See [EXT_003_INDEPENDENT_REVIEW_WAIVER.md](EXT_003_INDEPENDENT_REVIEW_WAIVER.md).
