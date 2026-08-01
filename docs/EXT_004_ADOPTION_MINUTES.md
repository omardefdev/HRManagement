# EXT-004 — Program adoption minutes (PKI/signature production binding)

| Field | Value |
|---|---|
| Card | EXT-004 (card 76) |
| Date | 2026-07-30 |
| Decision | Bind pki-signature-matrix into host startup and CI |
| Default live PKI | `LivePkiClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/ext-pki/pki-signature-matrix.json` (copied to `ExtPki/`) |
| Negative fixtures | missing-signer-role-fail, revoked-cert-fail, expired-without-ltv-fail, clock-skew-fail, key-exfil-fail, deprecated-alg-fail, form-signature-as-legal-fail |
| Positive fixtures | expired-cert-ltv-pass, tsa-outage-degrade-pass, archive-restore-verify-pass, signature-audit-pass, pki-run-ledger |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LivePkiClaim=true` | EXT-004a Accepted + national-pki-live Satisfied |
| Production HSM | EXT-004b + hsm-prod Satisfied |
| Live TSA | EXT-004c + tsa-live Satisfied |
| SIG-001 UI | EXT-004d |
| Treasury live signature bind | EXT-004e + update EXT-004 blocker in treasury matrix |
| CA legality (SYD-011) | EXT-004f + SYD-011 |

Until then: lab harness only; live PKI claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| PKI + Security | _pending wet-ink_ | |
| Legal Evidence | _pending wet-ink_ | |
| Integration Office | _pending wet-ink_ | |

## Independent review

See [EXT_004_INDEPENDENT_REVIEW_WAIVER.md](EXT_004_INDEPENDENT_REVIEW_WAIVER.md).
