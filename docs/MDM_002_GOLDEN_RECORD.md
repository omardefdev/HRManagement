# MDM-002 — Golden Person Record

## Purpose

Establish a single authoritative identity record (golden record) for every government employee across all agencies. PersonId is stable across merges and agency transfers; NationalIdHash links to civil registry without storing raw PII.

## Scope

- Person ID vs. employment IDs
- Identity proof and source precedence
- Multi-agency service history
- Merge/unmerge with full reversibility
- Survivorship rules and lineage
- Restricted national view
- Death/correction workflows

## Key Entities

| Entity | Location | Role |
|--------|----------|------|
| `GoldenPersonRecord` | `SharedKernel/MasterData/` | Authoritative person identity with status, provenance, merge history |
| `PersonMergeService` | `ServiceDefaults/MasterData/` | Domain service for reversible merge/unmerge |

## Design Decisions

1. **NationalIdHash only** — raw national ID is never stored; SHA-256 hex used for dedup.
2. **Merge history as JSON** — append-only log of merge/unmerge events with actor and timestamp.
3. **Provenance per field** — `ProvenanceJson` tracks which source system contributed each field value.
4. **Reversible merges** — unmerge restores the retired record and appends an unmerge event.
5. **Status enum** — Active / Merged / Deceased; Merged records point to survivor.

## Acceptance Criteria

- Duplicate identity does not create silently
- Agency transfer preserves history without unauthorized disclosure
- Merge is reversible; every value has provenance
- National access is separate and monitored

## Validation

- `scripts/mdm002-validate.ps1`
- `Mdm002GoldenRecordTests`
