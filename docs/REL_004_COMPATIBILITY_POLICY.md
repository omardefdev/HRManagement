# REL-004 — Release Policy and API/Event Compatibility

**Card:** REL-004  
**Version:** 2026.07.31.1  
**Status:** Lab binding (startup/CI) — mixed-version cluster drills deferred to REL-005  
**Aligns with:** [EXT_005_VERSIONING_AND_COMPAT.md](EXT_005_VERSIONING_AND_COMPAT.md), [EXT_005_SCHEMA_AND_EVENTS.md](EXT_005_SCHEMA_AND_EVENTS.md), [MESSAGE_EVENT_MATRIX.md](MESSAGE_EVENT_MATRIX.md)

---

## 1. Scope

This policy governs **semantic compatibility** for:

- Gateway-routed REST APIs (`/api/{service}/…`)
- Inter-service HTTP consumers (internal adapters, sagas, batch jobs)
- External partner APIs governed under EXT-005
- MassTransit message contracts in `HRManagement.MessageContracts`

It does **not** authorize live national OpenAPI publish (`national-openapi-publish` remains Blocked per EXT-005) or a production schema registry.

---

## 2. Version support model (N / N-1)

| Dimension | Rule |
|-----------|------|
| **Supported releases** | Current release **N** and previous minor/major **N-1** MUST coexist during rolling upgrades |
| **Cluster mixing** | A mixed-version cluster (some pods on N, some on N-1) MUST pass lab contract tests before promotion |
| **Consumer lag** | Certified consumers (EXT-005 CDC) MAY remain on N-1 for up to **two** release trains after deprecation notice |
| **Measurement** | Backward compatibility MUST be measured (`measureBackwardCompat=true`); silent breaks are forbidden |

Breaking changes without a major semver bump → **FAIL** (see EXT-005 `breaking-without-bump-fail`).

---

## 3. Deprecation window (two releases)

| Stage | Minimum duration | Action |
|-------|------------------|--------|
| **Announce** | Start of release **R** | Entry in [deprecation-register.json](rel-compat/deprecation-register.json); release notes; `Sunset` / `Deprecation` headers where applicable |
| **Warn** | Release **R+1** | Contract tests enforce N-1 path still green; telemetry on deprecated usage |
| **Remove** | Release **R+2** or later | Breaking removal allowed only after two full release trains AND ≥90 days notice (EXT-005 `noticeDaysMin`) |

Sunset of a published external contract requires a **major** version bump (`sunsetRequiresMajor`).

---

## 4. API change taxonomy (expand / migrate / contract)

| Class | Description | Semver | N-1 required? |
|-------|-------------|--------|---------------|
| **Expand** | Add optional field, new endpoint, new enum value with default | Minor | Yes — old clients ignore new fields |
| **Migrate** | Rename or reshape with dual-write/dual-read or feature flag | Minor + migration note | Yes — both shapes until R+2 |
| **Contract** | Remove field, tighten validation, change type, remove endpoint | Major | No — only after deprecation window |

All API changes MUST be recorded in the per-release [compatibility matrix](rel-compat/compatibility-matrix-template.md).

---

## 5. Event schema rules (additive-only within minor)

MassTransit contracts use `SchemaVersion` on each `I*` interface.

| Change | Allowed in minor? | Required action |
|--------|-------------------|-----------------|
| Add **optional** property | Yes | Bump `SchemaVersion` only if consumers must branch; document in event inventory |
| Add **required** property | **No** — treat as major | New interface or major contract version; N-1 consumers skip via `EventSchemaVersion` |
| Remove / rename property | **No** | Major + two-release deprecation |
| Change property type | **No** | Major |

Within a **minor** platform release, event schemas are **additive-only**. Producers MUST NOT emit required fields that N-1 consumers cannot ignore.

See [event-schema-inventory.json](rel-compat/event-schema-inventory.json) for the authoritative contract list.

---

## 6. Client inventory obligation

Every API consumer MUST appear in [api-client-inventory.json](rel-compat/api-client-inventory.json) with:

- `consumerType`: `gateway`, `inter-service`, or `external`
- `minSupportedApiVersion` / `maxTestedApiVersion`
- `owner` and `certificationStatus` (for external/partner)

New consumers MUST be registered before merge to `main`.

---

## 7. Contract tests and OpenAPI baselines

| Artifact | Purpose |
|----------|---------|
| [openapi-baseline/](rel-compat/openapi-baseline/) | Snapshot OpenAPI per service; PR diff via `scripts/rel004-openapi-diff.ps1` |
| `Rel004CompatibilityTests` | CI guard: inventories, docs, baselines, validate script |
| `scripts/rel004-compat-validate.ps1` | Pack validation (inventories, policy docs, deprecation register) |

Breaking OpenAPI diff on a minor release → **FAIL** unless accompanied by major bump and updated deprecation register.

---

## 8. Feature flags and release notes

- Behavioral changes behind feature flags MUST default to **off** in N-1 code paths until the flag is documented in release notes.
- Every release MUST ship [REL_004_RELEASE_NOTES_TEMPLATE.md](REL_004_RELEASE_NOTES_TEMPLATE.md) with API/event sections completed.
- Rollback constraints: [REL_004_ROLLBACK_POLICY.md](REL_004_ROLLBACK_POLICY.md).

---

## 9. Data rollback limits

Schema migrations that expand-only (add nullable column, add table) MAY roll back within the REL-005 rollback window. Contract migrations (drop column, tighten FK) require explicit waiver and MUST NOT block rollback without approved compensating action.

---

## 10. Verification (acceptance criteria mapping)

| Criterion | Evidence |
|-----------|----------|
| No breaking removal before two releases + notice | `deprecation-register.json` + validate script |
| Contract tests mandatory | `Rel004CompatibilityTests` in CI |
| Mixed-version cluster | Deferred lab drill — REL-005 progressive delivery |
| Schema migration rollback | `REL_004_ROLLBACK_POLICY.md` |
| Every release has matrix | `compatibility-matrix-template.md` per release tag |

---

## 11. Joins

| Join | Path |
|------|------|
| EXT-005 api-exchange matrix | [ext-api/api-exchange-matrix.json](ext-api/api-exchange-matrix.json) |
| External integrations | [EXTERNAL_INTEGRATIONS.md](EXTERNAL_INTEGRATIONS.md) |
| Event matrix | [MESSAGE_EVENT_MATRIX.md](MESSAGE_EVENT_MATRIX.md) |
| Gateway routes | `src/Gateway/HRManagement.Gateway/appsettings.json` → `ReverseProxy:Routes` |
