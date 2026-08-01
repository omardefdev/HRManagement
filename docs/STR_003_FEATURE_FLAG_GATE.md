# STR-003 — Feature flag exposure gate

Binds [REL_001_FEATURE_FLAGS.md](REL_001_FEATURE_FLAGS.md) `Gateway:EnabledModules` to product readiness in [`str-modules/module-catalog.json`](str-modules/module-catalog.json).

## Rules

1. **Explicit list:** every slug in `EnabledModules` must have `readinessStatus=Ready` (or signed exception in catalog `exceptions[]`).
2. **Empty list = all-on:** allowed only if every `pilotApprovedSlugs` entry is Ready (or waived). Otherwise set an explicit Ready-only list.
3. **Kill-switch:** removing a slug disables exposure; does not change catalog readiness.
4. **No unpublished modules:** do not add slugs absent from the catalog / Gateway published set (REL-001 exception path).
5. **Runtime note:** enforced by `ModuleCatalog` startup + `IModuleEntitlementGuard` + path entitlement (see [ADR-STR-003-production-module-binding.md](ADR-STR-003-production-module-binding.md)). CI = `str003-module-runtime-validate.ps1` + pack validate.

## Pilot approved set

Matches REL-001 list (14 app modules). Validate script asserts each is Ready unless exception.
