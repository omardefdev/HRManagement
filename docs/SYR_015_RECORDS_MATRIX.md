# SYR-015 — Syrian Records Retention and Disclosure Matrix

## Purpose

Define the certified Syrian national records retention matrix for all HR platform record classes, including duration, trigger, official copy, owner, legal hold, redaction, archive transfer, and certified destruction — sourced from Syrian authorities, not foreign standards.

## Scope

- Employee file, establishment register, attendance, payroll, tax, insurance, pension
- Administrative decision, investigation, complaint, signature evidence, technical audit log
- Duration, trigger, official copy, and owner per record class
- Legal hold blocking disposition
- Redaction rules aligned with Law 12/2024 disclosure limits
- Archive transfer and destruction with certificate
- Backup and analytics copy limits (no copy beyond official retention)

## Retention Matrix

The machine-readable matrix is at `docs/syr/records-retention-matrix.json`.

| Code | Record Class | Retention | Trigger |
|------|--------------|-----------|---------|
| employee-file | Employee personnel file | 75 years | Separation date |
| establishment-register | Establishment register | 50 years | Position closure |
| attendance-record | Time and attendance | 10 years | Fiscal year end |
| payroll-run | Payroll run | 15 years | Settlement date |
| tax-withholding | Tax withholding | 15 years | Fiscal year end |
| insurance-contribution | Social insurance | 50 years | Separation date |
| pension-payment | Pension payment | 75 years | Beneficiary death |
| administrative-decision | Admin decision | 30 years | Decision date |
| investigation-case | Investigation | 20 years | Case closure |
| complaint-grievance | Complaint | 10 years | Case closure |
| signature-evidence | Signature evidence | 15 years | Signature date |
| technical-audit-log | Technical log | 7 years | Log event date |

## Policies

### Legal Hold

- Disposition blocked while hold is active
- Hold requires reference and approver
- Propagation to database, object store, search, analytics, and backup

### Disclosure

- Governed by Law 12/2024 — no foreign standard import
- Redaction required before release
- Legal basis required for every disclosure

### Disposal

- Approval and certificate required
- Certificate fields: record class, volume, method, approver, witness hash, completion time
- No deletion under active hold

## Acceptance Criteria Mapping

- [x] Certified schedule from each competent authority — legal basis per class
- [x] No duration imported from foreign standard — Syrian legal references only
- [x] No deletion under hold — `legalHoldPolicy.blocksDisposition`
- [x] Disclosure respects Law 12/2024 — `disclosurePolicy`
- [x] Long-term signature verification — signature-evidence class
- [x] Copies/reports do not exceed original — backup/analytics policy per class
- [x] Every destruction has approval and evidence — `disposalPolicy`

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_015_RECORDS_MATRIX.md` | This document |
| `docs/syr/records-retention-matrix.json` | Machine-readable retention matrix |
| `scripts/syr015-validate.ps1` | Validates SYR-015 artifacts |
| `Syr015RecordsTests.cs` | Unit tests |

## Validation

- `scripts/syr015-validate.ps1`
- `Syr015RecordsTests`
