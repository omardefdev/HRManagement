# VEN-001 — Vendor Exit Drill & Operational Handover

**Card:** VEN-001  
**Status:** Done  
**Priority:** National Gate / XL  
**Owner:** System Owner + Alternate Operator

## Purpose

Define the full vendor exit drill checklist covering source/artifacts/IaC/data/docs/accounts/DNS/registry/KMS transfer, rebuild/restore, new certificates/keys, knowledge transfer, support continuity, vendor access revocation, deletion attestation, and Exit-RTO — complementing SOV-007 vendor-exit binding with a rehearsal plan.

## Scope

- Transfer: source code, artifacts, IaC, data, docs, accounts, DNS, registry, KMS
- Rebuild and restore from owned artifacts only
- New certificates and keys under national custody
- Knowledge transfer and support continuity
- Vendor access revocation
- Deletion attestation (prior vendor data wipe proof)
- Exit-RTO target and rehearsal steps

## Data Model

See `docs/ven/exit-drill-checklist.json` for the structured exit drill checklist.

## Business Rules

1. **Alternate operator builds, runs, and restores within Exit-RTO** — without critical vendor assistance.
2. **Export/import reconciled** — full data and attachment export verified.
3. **Vendor access revoked** — all accounts, keys, and DNS entries removed.
4. **Knowledge, code, and data owned and readable** — no vendor-only formats.
5. **Deletion attested** — prior vendor wipe proof documented.
6. **Full rehearsal required** — not tabletop only (live drill = Blocked until scheduled).

## Relationship to SOV-007 / IOP-002

| Card | Focus |
|------|-------|
| SOV-007 | Vendor-exit production binding Done |
| IOP-002 | Open portability Done |
| VEN-001 | Full exit drill checklist and Exit-RTO |

## Acceptance Criteria Mapping

- [x] Exit drill checklist with transfer, rebuild, revoke, attestation steps
- [x] Exit-RTO target defined
- [x] Export/import reconciliation criteria
- [x] Knowledge transfer and support continuity steps
- [x] Live drill blocked until rehearsal scheduled
- [x] Aligned with SOV-007 vendor-exit matrix

## Artifacts

| File | Purpose |
|------|---------|
| `docs/VEN_001_VENDOR_EXIT.md` | This document |
| `docs/ven/exit-drill-checklist.json` | Vendor exit drill checklist |
| `scripts/ven001-validate.ps1` | Validates VEN-001 artifacts |
| `Ven001ExitTests.cs` | Unit tests for exit drill checklist schema |

## Validation

- `scripts/ven001-validate.ps1`
- `Ven001ExitTests`
