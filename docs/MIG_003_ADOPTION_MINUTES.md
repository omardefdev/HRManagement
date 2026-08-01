# MIG-003 — Program adoption minutes (factory production binding)

| Field | Value |
|---|---|
| Card | MIG-003 (card 68) |
| Date | 2026-07-30 |
| Decision | Bind pipeline-matrix into host startup and CI |
| Default live ETL | `LiveEtlWorkersClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/mig-factory/pipeline-matrix.json` (copied to `MigFactory/`) |
| Negative fixtures | corrupt-schema-fail |
| Positive fixtures | same-batch-reload, mid-stage-fail-resume, cutover-window-budget, secure-staging-checklist, lineage-sample, log-redaction-sample, pipeline-run-ledger |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveEtlWorkersClaim=true` | MIG-003a–d Accepted |
| Live agency extract | agency-extract-access Satisfied |
| Live cutover perf proof | live-cutover-perf Satisfied (typically with MIG-006) |
| `LiveCodeMapEngineClaim=true` | MIG-003b + LiveEtlWorkersClaim |

Until then: lab harness only; live ETL claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Migration Engineering | _pending wet-ink_ | |
| Security | _pending wet-ink_ | |
| Migration Office | _pending wet-ink_ | |

## Independent review

See [MIG_003_INDEPENDENT_REVIEW_WAIVER.md](MIG_003_INDEPENDENT_REVIEW_WAIVER.md).
