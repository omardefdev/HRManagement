# PLAN-06 Execution — Payroll Determinism & Resume (AC-21, AC-22)

مرجع النطاق: [PLAN-06-payroll-determinism-resume.md](../acceptance-plans/PLAN-06-payroll-determinism-resume.md)

## W6.1 — AC-21

| Deliverable | Path | Status |
|---|---|---|
| Provenance DTO | `Payroll.Application/Dtos/PayrollRunProvenanceDto.cs` | Done |
| Provenance API | `GET /api/payroll/runs/{id}/provenance` | Done |
| Snapshots API | `GET /api/payroll/jobs/{jobId}/input-snapshots` | Done |
| Determinism comparer | `Payroll.Application/Services/PayrollDeterminismComparer.cs` | Done |
| PG integration test | `Pay021DeterminismPostgreSqlTests.cs` | Done |
| Acceptance script | `scripts/acceptance-payroll-determinism.ps1` | Done |
| Acceptance test | `Ac21PayrollDeterminismTests.cs` | Done |
| Acceptance seed | `AcceptancePayrollSeeder` + `SEED_ACCEPTANCE` on payroll-web | Done |

**Verify:** `RUN_TESTCONTAINERS=true dotnet test --filter Pay021DeterminismPostgreSqlTests`

## W6.2 — AC-22

| Deliverable | Path | Status |
|---|---|---|
| Chaos script | `scripts/acceptance-payroll-resume-chaos.ps1` | Done |
| PG resume test | `Pay022ResumePostgreSqlTests.cs` | Done |
| PG parallel perf | `Perf001PayrollBatchPostgreSqlTests.cs` | Done |
| Acceptance test | `Ac22PayrollResumeAfterKillTests.cs` | Done |
| Runbook link | `docs/PAYROLL_RUN_RESUME.md`, `nfr-chaos-safe-abort.ps1` | Done |

**Verify:** `RUN_CHAOS=true pwsh scripts/acceptance-payroll-resume-chaos.ps1` (live stack)

## Evidence contract

- AC-21: `acceptance-evidence/AC-21/` — `run-a-provenance.json`, `run-b-provenance.json`, `determinism-diff.json`, `result.json`
- AC-22: `acceptance-evidence/AC-22/db-checks/` — partitions before/after, salary-line-counts, `lease-conflict.json`, `timing.json`

## Notes

- Dual-run determinism uses **Run A → cancel (status=8) → Run B** same period (unique index `IX_PayrollRuns_LivePeriod`).
- CI uses pause/resume path; `RUN_CHAOS=true` uses `docker stop hrm-payroll-web` mid-partition.
