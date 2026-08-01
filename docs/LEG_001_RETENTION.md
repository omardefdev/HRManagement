# LEG-001 — Official Records Retention

## Purpose

Implement statutory records retention, legal hold, and disposition controls for official electronic records. No record under legal hold may be deleted regardless of retention schedule. All dispositions require authorization and produce evidence.

## Scope

- Official record / copy lifecycle
- Retention schedule (per record class)
- Legal hold (prevent disposition)
- Disclosure and redaction
- Archival formats and authenticity
- Hash/signature renewal for long-term verification
- Disposition approval and certificate
- E-discovery support
- System decommission records

## Key Entities

| Entity | Location | Role |
|--------|----------|------|
| `OfficialRecord` | `SharedKernel/Legal/` | Record subject to retention with class, hash, status, disposition tracking |
| `LegalHold` | `SharedKernel/Legal/` | Prevents disposition while active; scoped by record class/entity/date |

## Configuration

- `docs/leg/retention-schedule.json` — retention periods per record class with legal authority references

## Design Decisions

1. **Immutable records** — corrections create new records that supersede; no overwrite.
2. **Hold scope as JSON** — flexible filter over record class, entity type, entity ids, date ranges.
3. **Disposition requires approval** — `DispositionApprovedBy` and `DisposedAtUtc` must be set.
4. **Content hash** — SHA-256 ensures integrity over the retention period.

## Acceptance Criteria

- No destruction under hold
- Record + evidence retrievable within SLA
- Readability and verification throughout retention
- Every disposal has authorization and evidence
- Retention applies across DB / files / analytics / backups per policy

## Validation

- `scripts/leg001-validate.ps1`
- `Leg001RetentionTests`
