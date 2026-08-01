# DEC-001 — Legacy System Decommission

**Card:** DEC-001
**Status:** Done
**Priority:** P2-National / L
**Owner:** Migration Authority + Records Authority + Agency IT

## Overview

Defines the controlled decommission of legacy HR/payroll systems after migration to the national platform. Covers write-freeze, read-only transition, legally compliant archival, shadow operation detection, credential/license termination, and hardware/media destruction with attestation.

## Decommission Checklist

The machine-readable checklist is at `docs/dec/decommission-checklist.json`.

### Decommission Phases

| Phase | Description |
|-------|-------------|
| Write-Freeze | Legacy system stops accepting new data at announced cut-off date |
| Read-Only | System remains accessible for historical queries |
| Archive | Records extracted with manifest, checksum, and legal retention metadata |
| Shadow Audit | Field audit confirms no parallel data entry or shadow processes |
| Credential Revocation | All user accounts, service accounts, and API keys disabled |
| License Termination | Software licenses and support contracts formally ended |
| Media Destruction | Hardware and media destroyed with signed destruction certificate |
| Rollback Window | Time-limited fallback capability before final destruction |

### Archive Requirements

| Requirement | Description |
|-------------|-------------|
| Format | Open, documented format readable without legacy system |
| Manifest | Complete file listing with checksums |
| Retention | Per SYR-015 retention matrix |
| Verification | Sample retrieval test before destruction |
| Sovereignty | Archive stored within sovereign boundary |

### Shadow Operation Detection

- Field audit in at least two agencies per wave
- Check for: Excel spreadsheets, local databases, manual parallel entry, hidden integrations
- Any discovered shadow operation documented with exception and remediation timeline
- Zero shadow operations required for clean decommission; documented exceptions allowed

## Dependencies

MIG-005, MIG-006, LEG-001, FIN-001, SYR-012, SYR-015

## Artifacts

- `docs/dec/decommission-checklist.json`
- `scripts/dec001-validate.ps1`
- `Dec001DecommissionTests`
