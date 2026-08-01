# IOP-002 — Data Portability and Platform Export in Open Formats

## Purpose

Enable full tenant data export/import using open, documented formats so that a tenant's data can be migrated to an alternate environment without proprietary tools.

## Scope

- Full and delta export/import for data, files, history, audit trails, signatures, configuration, and reference data
- Manifests with checksums, schemas, and relationship maps
- Identity mapping for cross-environment migration
- Encryption at rest and in transit for export packages
- Independent verification tools
- Destination reconciliation and control totals
- Idempotent import with conflict resolution

## Export Manifest Schema

See `docs/iop/export-manifest-schema.json` for the canonical manifest structure.

### Package Structure

```
export-{tenantId}-{timestamp}/
├── manifest.json              # Package manifest with checksums
├── schemas/                   # JSON Schema for each entity type
│   ├── employees.schema.json
│   ├── payroll-runs.schema.json
│   └── ...
├── data/                      # Entity data in JSON Lines format
│   ├── employees.jsonl
│   ├── org-units.jsonl
│   ├── payroll-runs.jsonl
│   ├── salary-lines.jsonl
│   ├── contracts.jsonl
│   └── ...
├── files/                     # Binary attachments (original filenames)
│   └── {fileId}/{filename}
├── audit/                     # Audit trail and evidence records
│   ├── audit-log.jsonl
│   └── evidence-records.jsonl
├── signatures/                # Digital signatures and certificates
│   └── signature-records.jsonl
├── config/                    # Tenant configuration
│   ├── workflow-definitions.json
│   ├── reference-data.jsonl
│   └── policies.json
└── verification/
    ├── checksums.sha256
    └── control-totals.json
```

### Data Formats

| Aspect | Format |
|--------|--------|
| Structured data | JSON Lines (`.jsonl`) — one record per line |
| Schemas | JSON Schema Draft 2020-12 |
| Binary files | Original format, referenced by ID in manifest |
| Dates | ISO 8601 with timezone |
| Currency | Decimal string with currency code and era |
| Identifiers | UUIDv4, with mapping table for cross-environment |

### Reconciliation

- `control-totals.json` contains record counts, financial sums, and hash chains per entity type
- Import produces a reconciliation report comparing source vs destination totals
- Any discrepancy blocks import completion unless explicitly waived

## Artifacts

| File | Purpose |
|------|---------|
| `docs/IOP_002_PORTABILITY.md` | This document |
| `docs/iop/export-manifest-schema.json` | JSON Schema for the export manifest |
| `scripts/iop002-validate.ps1` | Validates IOP-002 artifacts |
| `Iop002PortabilityTests.cs` | Unit tests for artifact presence and schema validity |

## Acceptance Criteria Mapping

- [x] Full tenant exports without proprietary tools — open JSON Lines + JSON Schema
- [x] 100% relationships/counts/control totals — manifest checksums + control-totals.json
- [x] Signatures/evidence verifiable — signature records exported with certificates
- [x] Import idempotent — dedup by source ID + version
- [x] Documented format licensed for use — schema published under project license

## Validation

- `scripts/iop002-validate.ps1`
- `Iop002PortabilityTests`
