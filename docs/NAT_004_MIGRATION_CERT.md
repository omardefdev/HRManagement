# NAT-004 — Migration & Data Quality Certification

**Card:** NAT-004  
**Status:** Done (certification harness + evidence template)  
**Priority:** National Gate / L  
**Owner:** Internal Audit + Agency Data Owners

## Purpose

Define the migration and data quality certification pack: inventory/mapping/lineage, counts/totals/files reconciliation, duplicates/quarantine, security classification, cutover/rollback, legacy archive, and per-wave sign-offs.

## Scope

- MIG-001..007 evidence cross-references
- MIG-005/006 evidence pack requirements
- Zero critical record/financial gaps
- 100% accounted records (accepted/quarantined/rejected)
- Rollback tested in harness; live rollback Blocked
- Per-wave agency data owner sign-offs

## Data Model

See `docs/nat/nat004-checklist.json` for structured migration certification checklist.

## Business Rules

1. **100% accounted** — every migrated record has disposition and reason.
2. **Zero critical gaps** — no unexplained record or financial gap.
3. **Rollback harness pass** — cutover runbook and rollback steps validated in repo.
4. **Per-wave sign-off** — each agency signs data quality for its wave.
5. **Legacy archive preserved** — source systems read-only with provenance.

## Relationship to MIG-001..007

| Card | Focus |
|------|-------|
| MIG-001..007 | Migration factory, match, cutover, evidence bindings |
| NAT-004 | Independent migration/data quality certification template |

## Acceptance Criteria Mapping

- [x] MIG reference list and evidence item crosswalk
- [x] Data quality criteria (100% accounted, zero critical gap)
- [x] Rollback harness flag and legacy archive requirement
- [x] Per-wave sign-off requirement defined
- [x] Live statistical samples and wave sign-offs = Blocked

## Artifacts

| File | Purpose |
|------|---------|
| `docs/NAT_004_MIGRATION_CERT.md` | This document |
| `docs/nat/nat004-checklist.json` | Migration certification checklist |
| `scripts/nat004-validate.ps1` | Validates NAT-004 artifacts |
| `Nat004MigrationCertTests.cs` | Unit tests for migration cert schema |

## Validation

- `scripts/nat004-validate.ps1`
- `Nat004MigrationCertTests`

## Remaining Live Blockers

- Agency data owner sign-offs per wave pending
- Independent statistical sample on production data pending
