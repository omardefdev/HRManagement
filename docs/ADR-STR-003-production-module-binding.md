# ADR-STR-003 — Module catalog production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-003 (card 61) |
| Date | 2026-07-30 |
| Supersedes | Module catalog pack closure as insufficient for production |
| Dependencies | STR-001 production scope binding; REL-003; REL-001 flags |

## Context

The prior **module catalog pack** published `docs/str-modules/module-catalog.json` and structural validate harnesses but did not bind readiness into Gateway exposure. `Gateway:EnabledModules` empty meant all-on without Ready enforcement; path authorization checked roles only.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/str-modules/module-catalog.json` (copied as `ModuleCatalog/module-catalog.json`) |
| Runtime | `ModuleCatalogOptions` + `IModuleCatalogProvider` + `IModuleEntitlementGuard` in ServiceDefaults |
| Gateway tiles | `GatewayModuleCatalog` filters via entitlement guard |
| Path | `EnforcePathEntitlement` denies non-entitled `/ui`/`/api` module segments |
| Staging/Production | `EnforceOnStartup=true`; Gateway also `EnforcePathEntitlement=true` |
| Closure | **Done (production module binding)** |
| Still deferred | ONB-001; license marketplace; PROD-001 |

## Consequences

- NotReady modules cannot be enabled or path-accessed when enforcement is on.
- CI runs `str003-module-runtime-validate.ps1` for Ready evidence + Gateway slug drift.
- Pack validate remains for structural inventory.
