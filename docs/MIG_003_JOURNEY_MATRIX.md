# MIG-003 — Journey matrix

Source: [`mig-factory/pipeline-matrix.json`](mig-factory/pipeline-matrix.json) — [ADR-MIG-003](ADR-MIG-003-secure-reentrant-migration-factory.md).

## IdempotentReload

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| BatchKeyDefined | Yes | migration-engineering | idempotency |
| SameBatchNoDup | Yes | qa | same-batch-reload |

## MidFailResume

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CheckpointStore | Yes | migration-engineering | checkpoint |
| ResumeFixture | Yes | qa | mid-stage-fail-resume |

## RecordLineage

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RequiredFields | Yes | data | lineage.perRecordFields |
| LineageSample | Yes | qa | lineage-sample |

## NoPiiInLogs

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ForbiddenList | Yes | security | piiLogging |
| RedactionSample | Yes | qa | log-redaction-sample |

## CutoverThroughput

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| BudgetDeclared | Yes | migration-engineering | throughput |
| PerfStub | Yes | qa | cutover-window-budget |

## SecureStagingReview

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ZonesEncrypted | Yes | security | stagingZones |
| ReviewChecklist | Yes | qa | secure-staging-checklist |

Soft blockers: agency-extract-access, live-cutover-perf = Blocked.
