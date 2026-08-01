# EXT-002 — Program adoption minutes (civil-registry production binding)

| Field | Value |
|---|---|
| Card | EXT-002 (card 74) |
| Date | 2026-07-30 |
| Decision | Bind civil-registry-matrix into host startup and CI |
| Default live MOI | `LiveMoiClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/ext-civil/civil-registry-matrix.json` (copied to `ExtCivil/`) |
| Negative fixtures | purpose-mismatch-fail, excess-field-store-fail, outage-as-truth-fail, bulk-harvest-fail, stale-as-truth-fail |
| Positive fixtures | mismatch-review-pass, unavailable-degrade-pass, correction-workflow-pass, query-audit-pass, civil-registry-run-ledger |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveMoiClaim=true` | EXT-002a Accepted + moi-civil-registry-live Satisfied |
| Certified sandbox | EXT-002b + live-sandbox-cert Satisfied |
| Production rate/audit sink | EXT-002c |
| Correction UI | EXT-002d |

Until then: lab harness only; live MOI claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Integration + Privacy | _pending wet-ink_ | |
| Data Protection Officer | _pending wet-ink_ | |
| Migration Office | _pending wet-ink_ | |

## Independent review

See [EXT_002_INDEPENDENT_REVIEW_WAIVER.md](EXT_002_INDEPENDENT_REVIEW_WAIVER.md).
