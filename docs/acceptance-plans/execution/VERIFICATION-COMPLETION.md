# Acceptance verification completion (AC-01..AC-37)

Meta-plan execution record: all 14 foundation/feature plans wired into a single live verification path.

## Quick start

```powershell
# Full stack + all acceptance tests + evidence summary
pwsh scripts/acceptance-run-all.ps1

# Stack already up
pwsh scripts/acceptance-run-all.ps1 -SkipStackUp

# Single scenario
pwsh scripts/acceptance-run-all.ps1 -SkipStackUp -Filter "FullyQualifiedName~Ac01CentralTenantIsolationTests"
```

Environment variables:

| Variable | Default | Purpose |
|----------|---------|---------|
| `RUN_ACCEPTANCE` | `true` (set by script) | Enables live acceptance tests |
| `GATEWAY_BASE_URL` | `http://localhost:5000` | Gateway entry point |
| `KEYCLOAK_BASE_URL` | `http://localhost:8088` | Token grant |
| `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET` | from compose | Password grant |
| `ACCEPTANCE_PERSONA_PASSWORD` | `Accept-Hr123!` | Persona passwords |
| `ACCEPTANCE_EVIDENCE_DIR` | `acceptance-evidence/` | Evidence output root |

## Plan coverage matrix

| Plan | AC scenarios | Acceptance test classes | Supporting scripts |
|------|--------------|-------------------------|-------------------|
| PLAN-00 | harness | `AcceptanceCoverageTests`, `AcceptanceTenantConsistencyTests`, `AcceptanceEvidenceSummaryTests` | `acceptance-up.ps1`, `acceptance-run-all.ps1`, `acceptance-evidence-summarize.ps1` |
| PLAN-01 | AC-01..06 | `Ac01`..`Ac06` | — |
| PLAN-02 | AC-09,10 | `Ac09`, `Ac10` | — |
| PLAN-03 | AC-11,12 | `Ac11`, `Ac12` | — |
| PLAN-04 | AC-13,14 | `Ac13`, `Ac14` | — |
| PLAN-05 | AC-15..18 | `Ac15`..`Ac18` | `acceptance-correlation-trace.ps1`, `acceptance-inbox-duplicate.ps1`, `acceptance-rabbit-outbox-chaos.ps1`, `acceptance-consumer-catchup.ps1` |
| PLAN-06 | AC-21,22 | `Ac21`, `Ac22` | `acceptance-payroll-determinism.ps1`, `acceptance-payroll-resume-chaos.ps1` |
| PLAN-07 | AC-26,31 | `Ac26`, `Ac31` | `ac026-scan-pii.ps1` |
| PLAN-08 | AC-07,08 | `Ac07`, `Ac08` | `acceptance-quarantine-flow.ps1` |
| PLAN-09 | AC-19,20 | `Ac19`, `Ac20` | `acceptance-analytics-reconcile.ps1`, `acceptance-verify-db-isolation.ps1` |
| PLAN-10 | AC-23..25 | `Ac23`..`Ac25` | — |
| PLAN-11 | AC-27..30 | `Ac27`..`Ac30` | `acceptance-file-scan.ps1`, `acceptance-s2s-negative.ps1` |
| PLAN-12 | AC-32,33 | `Ac32`, `Ac33` | `acceptance-logistics-custody.ps1` |
| PLAN-13 | AC-34..37 | `Ac34`..`Ac37` | `acceptance-backup-restore.ps1`, `acceptance-perf-run.ps1`, `acceptance-pdf-arabic.ps1`, `acceptance-audit-journey.ps1` |

## Evidence contract

Each scenario writes `acceptance-evidence/AC-XX/` with eight required artifacts (see [README](../README.md)). `EvidenceRecorder` fills missing `db-checks.json` / `timing.json` placeholders on `WriteResult`. Aggregate summary: `acceptance-evidence/summary.md` via `acceptance-evidence-summarize.ps1`.

## CI

[`.github/workflows/ci.yml`](../../../.github/workflows/ci.yml) job `acceptance-live` runs `acceptance-up.ps1`, builds and executes `HRManagement.AcceptanceTests` with `RUN_ACCEPTANCE=true`, uploads `acceptance-evidence/**` as artifact.

## Thesis register sync

After a green run with Pass evidence:

```powershell
pwsh scripts/sync-thesis-capability-verification.ps1 -Apply
```

Maps AC Pass results to capability rows in `docs/THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md`.

## Deferred items (documented, not Fail)

AC-24/25 bank channel, AC-28 institutional CDR, AC-26 production HSM, AC-35 guaranteed numeric SLO — mark `Deferred` in `result.json` when mechanism-only proof is sufficient.

## Verification checklist

- [ ] `pwsh scripts/acceptance-up.ps1` — stack healthy
- [ ] `dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcceptanceCoverageTests` — 37 classes present
- [ ] `pwsh scripts/acceptance-run-all.ps1` — live tests + summary
- [ ] `acceptance-evidence/summary.md` — all executed AC rows
- [ ] `pwsh scripts/sync-thesis-capability-verification.ps1 -Apply` — register updated where Pass
