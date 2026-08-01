# SYR-012 — Legacy Record Unification & Provenance Quarantine

**Card:** SYR-012
**Status:** Done
**Priority:** P1-National / L
**Owner:** Migration Authority + Data Governance + Records Authority

## Overview

Defines how records from heterogeneous legacy systems (ministry spreadsheets, standalone databases, paper-digitized archives, parallel payroll ledgers) are unified into the national HR platform with full provenance tracking. Records that cannot be automatically matched or whose integrity is suspect are quarantined rather than silently ingested.

## Provenance Model

The machine-readable provenance/quarantine model is at `docs/syr/provenance-quarantine-model.json`.

### Record Provenance Fields

| Field | Description |
|-------|-------------|
| `sourceSystem` | Originating system identifier |
| `sourceRecordId` | Original record ID in source |
| `extractedAt` | Timestamp of extraction |
| `extractedBy` | Person/process that extracted |
| `hashAtExtraction` | SHA-256 of original record |
| `transformationLog` | Ordered list of transformations applied |
| `confidenceScore` | 0.0–1.0 match confidence |
| `quarantineReason` | Null if clean; reason code if quarantined |

### Quarantine Reasons

| Code | Reason |
|------|--------|
| QR-01 | Duplicate candidate — confidence below threshold |
| QR-02 | Missing mandatory field (national ID, legal regime) |
| QR-03 | Hash mismatch between extraction and load |
| QR-04 | Source system not in approved registry |
| QR-05 | Date/period inconsistency across records |
| QR-06 | Amount reconciliation failure |
| QR-07 | Manual override without authorization evidence |

## Unification Rules

- Every imported record carries full provenance from source to unified store
- No record is silently merged; duplicate candidates enter quarantine for human review
- Quarantined records block payroll processing for the affected employee until resolved
- Resolution requires four-eyes approval and creates an immutable resolution audit entry
- Source hash verified at extraction, transport, and load — any mismatch quarantines the record
- Transformation log is append-only and versioned

## Reconciliation

- Source system record counts must match extracted counts (control totals)
- Loaded records + quarantined records = extracted records (no silent loss)
- Monthly reconciliation report comparing source, quarantine, and unified store
- Unresolved quarantine items older than 90 days escalate to Data Governance Board

## Dependencies

MIG-004, MIG-005, MDM-002, SYR-009, LEG-001

## Artifacts

- `docs/syr/provenance-quarantine-model.json`
- `scripts/syr012-validate.ps1`
- `Syr012LegacyTests`
