# ADR-MIG-003 — Secure reentrant migration factory (hybrid gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-003 (card 68) |
| Date | 2026-07-30 |
| Dependencies | MIG-002 Satisfied; TEN-002 Satisfied; OPS-005 Satisfied |

## Context

MIG-002 provides signed code maps and quarantine policy. TEN-002 enforces tenant isolation. OPS-005 provides IaC staging environments. There is no migration factory contract for extract staging, encryption, schema validation, idempotent load, checkpoint/resume, per-record lineage, PII-safe logs, or cutover throughput budgeting. `DataImportController` remains a primitive CSV path.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/mig-factory/pipeline-matrix.json` |
| Depth | **Hybrid factory gate pack** — contracts + harness fixtures + validate; not live ETL workers |
| Stages | extract → validate → transform → quarantine → load → purge |
| Idempotency | batchKey + naturalKey; reloadPolicy skip-identical |
| Checkpoint | resumable mid-stage fail |
| Lineage | per-record fields required |
| Soft blockers | agency-extract-access; live-cutover-perf |
| Closure | Done (hybrid factory gate pack) |
| Out of scope | ETL workers; DataImportController rewrite; MIG-004/005/006 |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| MIG-003a | Staging I/O + encryption |
| MIG-003b | Transform worker consuming MIG-002 maps |
| MIG-003c | Idempotent load + ledger |
| MIG-003d | Observability + PII redaction runtime |

## Blocker register

| Id | Status | Note |
|---|---|---|
| MIG-002 | Satisfied | Code-map gate Done |
| TEN-002 | Satisfied | Tenant enforcement present |
| OPS-005 | Satisfied | IaC Done |
| agency-extract-access | Blocked | Lab harness only |
| live-cutover-perf | Blocked | Real window → MIG-006 + agency volumes |

## Consequences

- MIG-005 consumes lineage/rejects/ledger contracts from this pack.
- Validate fails on missing lineage, PII in log samples, non-idempotent reload, or unencrypted staging zone.
