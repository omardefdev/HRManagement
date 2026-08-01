# EXT-005 — Program adoption minutes (API/exchange production binding)

| Field | Value |
|---|---|
| Card | EXT-005 (card 77) |
| Date | 2026-07-30 |
| Decision | Bind api-exchange-matrix into host startup and CI |
| Default live exchange | `LiveExchangeClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/ext-api/api-exchange-matrix.json` (copied to `ExtApi/`) |
| Negative fixtures | missing-owner-sla-fail, breaking-without-bump-fail, shared-db-fail, certified-consumer-break-fail, purpose-denied-export-fail, schema-incompat-fail, deprecation-too-short-fail |
| Positive fixtures | n1-compat-pass, throttle-exceed-pass, interop-harness-pass, exchange-audit-pass, api-exchange-run-ledger |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveExchangeClaim=true` | EXT-005a Accepted + gov-exchange-bus-live Satisfied |
| schema-registry-live | EXT-005b |
| PROD-007 join | EXT-005c — Satisfied via PROD-007 production extensibility-platform binding (live runtime remains PROD-007a) |
| Partner CDC / certification | EXT-005d + EXT-006 + partner-cdc-cert |
| IOP/SYR profiles / governed OpenAPI | EXT-005e + national-openapi-publish where required |

Until then: lab harness only; live exchange claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| API Governance | _pending wet-ink_ | |
| Integration Architecture | _pending wet-ink_ | |
| Privacy / Export | _pending wet-ink_ | |

## Independent review

See [EXT_005_INDEPENDENT_REVIEW_WAIVER.md](EXT_005_INDEPENDENT_REVIEW_WAIVER.md).
