# SYR-010 — Government Data Bus & Transitional Integration

**Card:** SYR-010  
**Status:** Done  
**Priority:** P1-National / XL  
**Owner:** Government Integration Authority + Enterprise Architecture

## Purpose

Define the national government data bus profile and transitional integration channel for Syrian HR platform connectors. Until the official bus is available, all government integrations use signed, encrypted batch files with manifest, sequence, checksum, ack, replay, and reconciliation — with no shadow integrations.

## Scope

- Government data bus profile (when ready): mTLS, signatures, canonical IDs
- Transport modes: API, event, batch (transitional default)
- Schema registry binding per connector (IOP-001)
- Transitional signed encrypted file channel with manifest/sequence/checksum
- Ack/replay/reconciliation for 24-hour outage tolerance
- Cutover batch→API without semantic change
- Freshness dashboard, backlog, and control-total variance
- Prohibition of hidden/shadow integrations

## Data Model

See `docs/syr/data-bus-profile.json` for connector profiles, transport modes, SLOs, and transitional batch policy.

Each connector profile contains:

| Field | Description |
|-------|-------------|
| `connectorId` | Unique connector identifier |
| `authority` | Governing government authority |
| `classification` | Data classification level |
| `purpose` | Legal purpose binding (IAM-004/TEN-003) |
| `contractVersion` | Active contract/schema version |
| `transport` | `batch` / `api` / `event` |
| `slo` | Freshness, availability, reconciliation targets |
| `canonicalIds` | Mapped canonical identifier fields |
| `transitionalBatch` | Signed file channel parameters when bus unavailable |

## Business Rules

1. **Every connector has authority, classification, purpose, contract/version, and SLO** — no anonymous integrations.
2. **24-hour outage must not lose or duplicate data** — replay-safe with idempotency keys.
3. **Batch→API cutover preserves semantics** — same canonical mapping, versioned transition plan.
4. **`Live` claim requires official endpoint proof** — transitional batch cannot claim Live bus.
5. **Control totals reconciled** — freshness, backlog, and aggregate variance monitored.
6. **No shadow integrations** — all connectors registered in profile; CI fails on unregistered paths.

## Relationship to EXT-005 / INT-004 / IOP-001

| Card | Focus |
|------|-------|
| EXT-005 | Gov data-exchange API contract gate pack |
| INT-004 | Live connector mode-switched government connectors |
| IOP-001 | Schema registry profile templates |
| SYR-010 | National data bus profile + transitional batch channel |

## Acceptance Criteria Mapping

- [x] Connector profiles with authority, classification, purpose, contract/version, SLO
- [x] Transitional signed encrypted batch with manifest/sequence/checksum/ack/replay
- [x] Schema registry references per connector
- [x] Cutover plan batch→API documented
- [x] Live bus claim blocked until official endpoint proof
- [x] Reconciliation and control-total policy defined

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_010_DATA_BUS.md` | This document |
| `docs/syr/data-bus-profile.json` | Government data bus connector profiles |
| `scripts/syr010-validate.ps1` | Validates SYR-010 artifacts |
| `Syr010DataBusTests.cs` | Unit tests for data bus profile schema |

## Validation

- `scripts/syr010-validate.ps1`
- `Syr010DataBusTests`
