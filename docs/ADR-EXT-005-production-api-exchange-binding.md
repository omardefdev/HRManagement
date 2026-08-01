# ADR-EXT-005 — API/exchange production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-005 (card 77) |
| Date | 2026-07-30 |
| Supersedes | Gov data-exchange / API contract gate pack as insufficient for production |
| Dependencies | IAM-004 / TEN-003 / INT-002 Satisfied; PROD-007 Satisfied (production extensibility-platform binding) |

## Context

The prior pack published the api-exchange matrix, catalog/compat/purpose/consumer contracts, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `ExtApi` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/ext-api/api-exchange-matrix.json` |
| Runtime | `ExtApiOptions` + `IExtApiProvider` + `IApiExchangeClaimGuard` |
| Catalog | required fields including owner + SLA |
| Versioning / compat | semver; breaking requires major; N-1 measured |
| Shared DB | forbidden |
| Purpose export | consent + allowlist |
| Consumer-driven | certified consumers protected |
| Throttle / tenant / deprecation | 429 on exceed; tenant header; noticeDaysMin ≥ 90 |
| Live exchange | Default `LiveExchangeClaim=false`; forbidden until EXT-005a–e |
| Soft blockers | IAM-004/TEN-003/INT-002 Satisfied; PROD-007 Satisfied; gov-exchange-bus-live / schema-registry-live / partner-cdc-cert / national-openapi-publish Blocked |
| Closure | **Done (production api-exchange binding)** |

## Consequences

- Staging/Production fail startup on incomplete exchange policy, dishonest blockers, or live exchange claim.
- CI runs `ext005-api-exchange-runtime-validate.ps1`.
- Live gov bus, prod schema registry, EXT-006 certification UI, and PROD-007 remain out of scope.
