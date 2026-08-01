# SYR-009 — Civil Registry Integration and Identity Verification

## Purpose

Define Syrian civil registry identity fields, Arabic name matching rules, deduplication policy, civil event workflows, and HR boundary rules. Complements EXT-002 production civil-registry binding with national HR-specific match and identity governance.

## Scope

- Official national ID as primary identifier (11-digit numeric)
- Allowed query/store fields with forbidden excess storage
- Arabic name normalization (L10N-002) with original preservation
- Match rules with confidence levels (exact NID, name+birthdate, fuzzy name)
- Auto-merge forbidden for duplicates and conflicts
- Civil events: death, name change, birth correction workflows
- Outage policy: no permanent identity without review
- Barcode verification when available
- Query audit with retention; daily reconciliation SLA
- HR must not substitute for civil registry

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_009_CIVIL_REGISTRY.md` | This document |
| `docs/syr/civil-identity-match-rules.json` | Identity match and dedup rules |
| `scripts/syr009-validate.ps1` | Validates SYR-009 artifacts and structure |
| `Syr009CivilIdentityTests.cs` | Unit tests for civil identity schema and validation |

## Relationship to EXT-002

| Card | Focus |
|------|-------|
| EXT-002 | Production civil-registry connector binding, purpose/consent, lab harness |
| SYR-009 | National HR identity match rules, Arabic names, dedup, civil events, HR boundary |

## Acceptance Criteria Mapping

- [x] Sharing agreement fields, purpose, and SLA defined
- [x] No excess storage; confidence/reason for every match
- [x] Duplicate/conflict auto-merge forbidden
- [x] Death/name change/correction produce controlled workflows
- [x] Outage does not create permanent identity without review
- [x] Audit log of who queried and why

## Validation

- `scripts/syr009-validate.ps1`
- `Syr009CivilIdentityTests`
