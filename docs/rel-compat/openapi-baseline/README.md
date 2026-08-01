# OpenAPI baseline snapshots (REL-004)

**Card:** REL-004  
**Purpose:** Store governed OpenAPI snapshots per service for PR/release diffing.

---

## Why baselines exist

Dev Swagger (`AddHrOpenApi` / `UseHrOpenApi`) is **not** governed national publish (EXT-005 `national-openapi-publish` = Blocked). These baselines are **lab contract artifacts** used by:

- `scripts/rel004-openapi-diff.ps1` — compare baseline vs candidate spec; flag breaking changes
- `Rel004CompatibilityTests` — ensure baselines exist and validate script passes
- Per-release [compatibility matrix](compatibility-matrix-template.md)

---

## Layout

```
docs/rel-compat/openapi-baseline/
  README.md
  hr-core-v1.openapi.json      ← sample stub
  payroll-v1.openapi.json      ← add when service OpenAPI is exported
  ...
```

Naming: `{service}-v{major}.openapi.json`

---

## Updating a baseline

1. Export OpenAPI from the service (development Swagger or build-time generator).
2. Run diff against current baseline:
   ```powershell
   powershell -File scripts/rel004-openapi-diff.ps1 `
     -Baseline docs/rel-compat/openapi-baseline/hr-core-v1.openapi.json `
     -Candidate path/to/new-spec.json
   ```
3. If diff is **expand-only** (minor): update baseline in same PR with matrix row.
4. If diff is **breaking**: major bump, deprecation register entry, two-release window — or reject.

---

## Sample stub

[hr-core-v1.openapi.json](hr-core-v1.openapi.json) is a minimal OpenAPI 3.0 stub representing the employee read surface. Replace with full export when national publish unblocks (EXT-005a).

---

## Joins

| Doc | Role |
|-----|------|
| [REL_004_COMPATIBILITY_POLICY.md](../REL_004_COMPATIBILITY_POLICY.md) | N/N-1 and expand/migrate/contract rules |
| [EXT_005_CATALOG_AND_METADATA.md](../EXT_005_CATALOG_AND_METADATA.md) | Governed publish vs dev Swagger |
| [api-client-inventory.json](../api-client-inventory.json) | Consumer version constraints |
