# PLAN-25 — Baseline (P0)

**Date:** 2026-10-29
**Branch:** `feature/plan-25-production-approvals`, created from `main` @ `3893a5d` (PLAN-33 merged; `reports/plan33-final-report.md` present).

## Build

| Command | Result |
|---------|--------|
| `dotnet build HRManagement.slnx` | **0 errors**, 4 warnings — all `NU1903` (SSH.NET 2025.1.0 advisories GHSA-mggc-4xg6-vcxf / GHSA-q939-rpr3-3284, `HRManagement.IntegrationTests`) |
| `dotnet build HRManagement.slnx -warnaserror` | **fails at restore**: the same two `NU1903` advisories become errors (`HRManagement.IntegrationTests.csproj`). Pre-existing, not introduced by this plan (decision 9: only warnings this plan introduces are fixed). No compiler warning is reported once restore succeeds. |

## Unit tests

Filter (prompt P0, as `FullyQualifiedName~` clauses): `Hcm02|Hcm03|Wf0|Workflow|SoD|Est0|Policy|Architecture`

| Total | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| 1535 | 1534 | 1 | 0 |

The one failure is the known baseline `BolaArchitectureTests.Employee_scoped_controller_actions_apply_org_scope_guard`
(HR.Contracts `Index` actions; recorded in PLAN-31/32/33 baselines). Any other failure after a PLAN-25 change is a regression.

## Integration tests (same filter)

| Total | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| 73 | 68 | 3 | 2 |

| Failure | Reason | Pre-existing? |
|---------|--------|---------------|
| `Hcm028AppealGovernanceMigrationTests.Legacy_appeal_notes_become_decided_legacy_reopen_appeals_without_changing_applications` | ar-SA / Um Al-Qura culture writes a Hijri year into SQL (passes with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`) | yes — environment |
| `SoDForgedApproverTests.TransferApprove_IgnoresForgedFormField_UsesTokenOnly` | `ArgumentNullException (source)` in the HR.Movements host set-up | yes — not touched by PLAN-25 |
| `EndToEndReleaseWorkflowSmokeIntegrationTests.ReleaseWorkflow_Contract_Leave_PayrollPost_Separation_ChainSucceeds` | HR.Contracts DI: `IHrCoreActiveRegimeClient` not registered in the test host | yes — not touched by PLAN-25 |

Skipped: the two opt-in scale tests (`Hcm031CompetitionScaleTests`, `Hcm032RecruitmentAnalyticsScaleTests`).

Raw logs (not committed): `artifacts/plan25/{build,build-warnaserror,unit-baseline,int-baseline}.log`.
