# ADR-EXT-002 — Civil-registry production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-002 (card 74) |
| Date | 2026-07-30 |
| Supersedes | Civil-registry contract gate pack as insufficient for production |
| Dependencies | EXT-001 production federation binding; GOV-001; MIG-004 |

## Context

The prior pack published the civil-registry matrix, purpose/minimal/audit contracts, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `ExtCivil` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/ext-civil/civil-registry-matrix.json` |
| Runtime | `ExtCivilOptions` + `IExtCivilProvider` + `ICivilRegistryClaimGuard` |
| Purpose | allowlist + consentRequired |
| Minimal | request/response field allowlists |
| Mismatch | human workflow required |
| Cache / unavailable | forbid stale/unverified as truth |
| Rate | bulkHarvestForbidden; maxQueriesPerMinute > 0 |
| Audit | required fields per query |
| Live MOI | Default `LiveMoiClaim=false`; forbidden until EXT-002a–d |
| Soft blockers | EXT-001/GOV-001/MIG-004 Satisfied; moi-civil-registry-live / live-sandbox-cert / national-idp-live Blocked; L10N-002 NotStarted |
| Closure | **Done (production civil-registry binding)** |

## Consequences

- Staging/Production fail startup on incomplete civil-registry policy, dishonest blockers, or live MOI claim.
- CI runs `ext002-civil-registry-runtime-validate.ps1`.
- Live MOI connector, certified sandbox, and correction UI remain out of scope.
