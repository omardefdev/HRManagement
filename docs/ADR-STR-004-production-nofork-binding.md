# ADR-STR-004 — No-fork production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-004 (card 62) |
| Date | 2026-07-30 |
| Supersedes | Nofork policy pack closure as insufficient for production |
| Dependencies | STR-002 Satisfied; TEN-003 Satisfied; STR-003 production module binding |

## Context

The prior pack published taxonomy, dual-agency fixtures, and a validate harness but did not bind packs into runtime entitlement/branding or CI release gates. `Gateway:EnabledModules` remained process-global.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/str-product/nofork-matrix.json` + `testdata/str004/agencies/*.json` |
| Runtime | `NoForkOptions` + `INoForkPolicyProvider` + `IAgencyConfigResolver` |
| Variation | Branding + optional module lists per `tenantId`; same `releaseDigest` |
| STR-003 join | Agency pack further restricts optional modules after Ready/flags |
| Soft blockers | PROD-001 and ONB-001 remain **Blocked** |
| Closure | **Done (production nofork binding)** |

## Consequences

- Staging/Production fail startup on digest mismatch or missing pack schema fields.
- CI runs `str004-nofork-runtime-validate.ps1` (patterns + dual digest + appsettings).
- Crypto-signed provision and versioned policy engines remain ONB-001 / PROD-001.
