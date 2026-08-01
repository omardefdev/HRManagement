# ADR-EXT-005 — Government data exchange / API contracts (gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-005 (card 77) |
| Date | 2026-07-30 |
| Dependencies | IAM-004 Satisfied; TEN-003 Satisfied; INT-002 Satisfied; PROD-007 Satisfied (production extensibility-platform binding) |

## Context

IAM-004 provides S2S identity and least privilege. TEN-003 provides cross-tenant isolation. INT-002 provides inbox/idempotency. Platform has Level-2 File/Stub adapters (`EXTERNAL_INTEGRATIONS`), dev-only OpenAPI, internal event matrix, and gateway rate limiting — but there is no signed contract for a governed integration catalog (owner/SLA), measured backward compatibility, prohibition of shared-DB joins, purpose-bound export, consumer-driven protection of certified consumers, lab schema-registry compatibility, throttling/correlation/error model, or deprecation windows. PROD-007 (stable product extension platform) is Satisfied as production extensibility-platform binding (live extension runtime remains deferred). EXT-006/007 will consume this catalog.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/ext-api/api-exchange-matrix.json` |
| Depth | **Gov data-exchange / API contract gate pack** — catalog contracts + mock registry harness; not live gov bus / prod schema registry |
| Port join | EXTERNAL_INTEGRATIONS + OpenAPI/events/IAM-004/TEN-003 (docs/lab only; no Domain/Gateway rewrite) |
| Soft blockers | PROD-007; gov-exchange-bus-live; schema-registry-live; partner-cdc-cert; national-openapi-publish |
| Closure | Done (gov data-exchange / API contract gate pack) |
| Out of scope | Live bus; prod registry; EXT-006 cert UI; EXT-007 sector catalog product; shared DB; Domain/Gateway rewrite |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| EXT-005a | Live catalog publish |
| EXT-005b | schema-registry-live |
| EXT-005c | PROD-007 join |
| EXT-005d | EXT-006 certification surface |
| EXT-005e | IOP-001 / SYR-010 profiles |

## Blocker register

| Id | Status | Note |
|---|---|---|
| IAM-004 | Satisfied | S2S Done |
| TEN-003 | Satisfied | Isolation Done |
| INT-002 | Satisfied | Inbox Done |
| PROD-007 | Satisfied | Production extensibility-platform binding Done |
| gov-exchange-bus-live | Blocked | Lab catalog only |
| schema-registry-live | Blocked | Lab mock registry only |
| partner-cdc-cert | Blocked | Needs EXT-006 |
| national-openapi-publish | Blocked | Dev Swagger ≠ governed publish |

## Consequences

- EXT-006/007, IOP-001, SYR-010 consume catalog/compat/purpose contracts from this pack.
- Validate fails on missing owner/SLA, breaking without major, shared DB, certified-consumer break, purpose-denied export, or wrongly Satisfied soft blockers.
