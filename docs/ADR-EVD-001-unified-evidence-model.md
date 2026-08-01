# ADR-EVD-001 — Unified Evidence Model

## Status

Accepted — 2026-07-31

## Context

Audit evidence is split across `EntityAuditLog`, Gateway audit/security events, Auth audit, File access audit, and Archive custody events. Legal/audit reconstruction of hire/transfer/payroll/role-change requires a common schema with correlation, hash chain, and immutability.

## Decision

1. Introduce `EvidenceRecord` in SharedKernel as the **unified decision evidence** type (interface-over-existing + new store), not a forced merge of all six legacy tables in one migration.
2. Seal rows with SHA-256 (`EvidenceHashChain`) keyed by `ChainKey` + monotonic `Sequence`.
3. Correlate via OpenTelemetry/business `CorrelationId` and optional `CausationId`.
4. Enforce append-only via `EvidenceImmutabilityInterceptor` (+ DB REVOKE in ops runbooks).
5. Legacy audit tables remain; critical journeys **also** emit `EvidenceRecord` via `IEvidenceCollector`.
6. `EntityAuditLog` stays globally queryable for platform-admin; evidence rows are tenant-scoped.

## Consequences

- Services opt in with `ConfigureEvidenceRecords()` + `EvidenceCollector` DI.
- Downstream EVD-002/EVD-003 consume the same schema for forensics and the evidence repository.
- Trusted time beyond `system-clock` (NTP/TSA) is a follow-on hardening note in TimeSource.
