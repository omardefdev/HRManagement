# ORG-001 — Dated Org Structure

## Purpose

Implement temporal (dated) org-unit versioning so that every structural change (create, rename, move, merge, split, close) produces a new version with explicit validity dates. History is never overwritten; as-of queries resolve the active version for any point in time.

## Scope

- Create / merge / split / rename / close / move operations
- Future-effective changes
- As-of temporal queries
- Approval and impact analysis before execution
- Cancellation of pending changes
- No cycles; org snapshots

## Key Entity

| Entity | Location | Role |
|--------|----------|------|
| `OrgUnitVersion` | `HR.Core.Domain/Entities/` | Dated version with ValidFrom/ValidTo, Name, ParentId, OperationType |

## Database

- Migration: `20260731170000_ORG001_DatedOrgStructure`
- DbSet: `HRCoreDbContext.OrgUnitVersions`
- Indexes on `(OrgUnitId, ValidFrom)` and `(TenantId, OrgUnitId, ValidFrom)`
- FK to `OrgUnits` with Restrict delete
- RLS enabled via `TenantRlsMigrationHelper`

## Design Decisions

1. **Separate version table** — `OrgUnit` remains the stable identity; `OrgUnitVersion` captures temporal state.
2. **Open-ended ValidTo** — null means current/active; closing a version sets `ValidTo`.
3. **OperationType enum** — Create, Rename, Move, Merge, Split, Close.
4. **Decision reference** — every structural change traces to a decree or approval.

## Acceptance Criteria

- History is never rewritten; as-of reports are correct
- Impact is shown before approval
- Changes are cancellable before effective date
- Subtree moves are atomic and governed

## Validation

- `scripts/org001-validate.ps1`
- `Org001DatedStructureTests`
