# ADR-MIG-003 — Factory production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-003 (card 68) |
| Date | 2026-07-30 |
| Supersedes | Hybrid factory gate pack as insufficient for production |
| Dependencies | MIG-002 production codemap binding |

## Context

The prior pack published the pipeline matrix, staging/idempotency/checkpoint/lineage/PII contracts, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `MigFactory` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/mig-factory/pipeline-matrix.json` |
| Runtime | `MigFactoryOptions` + `IMigFactoryProvider` + `IEtlWorkersClaimGuard` |
| Stages | extract → validate → transform → quarantine → load → purge |
| Staging | encryptionAtRest + tenantScoped + purgePolicy + accessRole |
| Idempotency | `skip-identical` + `duplicateOnReloadForbidden` |
| Checkpoint | `resumeFromStage=true` |
| Lineage / PII | required per-record fields; forbidden PII in logs |
| Throughput | lab budget; `liveProofRequired=false` while live-cutover-perf Blocked |
| Live ETL | Default `LiveEtlWorkersClaim=false`; forbidden until MIG-003a–d |
| Soft blockers | MIG-002/TEN-002/OPS-005 Satisfied; agency-extract-access/live-cutover-perf Blocked |
| Closure | **Done (production factory binding)** |

## Consequences

- Staging/Production fail startup on incomplete factory contracts, dishonest blockers, live ETL claim, or liveProof while Blocked.
- CI runs `mig003-factory-runtime-validate.ps1`.
- ETL workers (MIG-003a–d), DataImportController rewrite, MIG-004/005/006 live paths remain out of scope.
- `MigCanonical:LiveCodeMapEngineClaim` stays false until MIG-003b / LiveEtlWorkersClaim.
