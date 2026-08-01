# EVD-003 — Evidence Repository: Build & Operate

**Card:** EVD-003
**Status:** Done
**Priority:** P1-National / L
**Owner:** Assurance Office + Platform

## Overview

Builds and operates the immutable evidence repository that stores all control evidence, test results, security scan outputs, operational drill records, and compliance artifacts. The repository is the single source of truth for NAT-001 evidence packs and all national authorization gates. It enforces immutability, automated collection, freshness tracking, PII/secret exclusion, and independent assessor read-only access.

## Evidence Store Model

The machine-readable model is at `docs/evd/evidence-store-model.json`.

### Evidence Item Schema

| Field | Type | Description |
|-------|------|-------------|
| `id` | uuid | Unique evidence identifier |
| `cardId` | string | Associated card (e.g., SOV-001) |
| `controlId` | string | Control or requirement identifier |
| `releaseDigest` | string | Release version the evidence applies to |
| `source` | string | CI/CD pipeline, test run, manual upload |
| `collectedAt` | ISO 8601 | Automated collection timestamp |
| `collectedBy` | string | System or person |
| `hashAlgorithm` | string | SHA-256 |
| `hashValue` | string | Content hash |
| `classification` | enum | confidential / restricted / internal |
| `expiresAt` | ISO 8601 | Evidence expiration date |
| `status` | enum | Active / Expired / Superseded |

### Immutability Rules

- Evidence items are append-only; no update or delete by any user including admin
- Hash chain links each item to its predecessor (EVD-001 model)
- Storage backend enforces write-once-read-many (WORM) semantics
- Backup and restore preserve full hash chain integrity

### Automated Collection

- CI/CD pipeline publishes evidence on successful build/test/scan
- Security scan results collected automatically after each scan
- Operational drill evidence captured with drill identifier
- Manual evidence upload requires reviewer approval and hash verification

### Access Control

- Evidence authors: write-only (cannot modify after write)
- Application admin: no write, no delete
- Independent assessor: read-only access without production system access
- Audit log for all access operations

### Freshness & Expiration

- Each evidence type has a configurable expiration period
- Expired evidence creates a visible gap in the evidence index
- Gap notifications sent to evidence owner and assurance office
- Re-collection required before authorization gate

## Dependencies

NAT-001, EVD-001, OBS-003, SOV-002, BLD-001

## Artifacts

- `docs/evd/evidence-store-model.json`
- `scripts/evd003-validate.ps1`
- `Evd003RepositoryTests`
