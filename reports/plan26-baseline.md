# PLAN-26 — Local baseline (session 1, Step 0)

**Date:** 2026-10-21
**Branch:** `feature/plan-26-assessment-interview-requisition`
**Snapshot commit:** `544ba7a chore: snapshot working tree before PLAN-26`

## Environment notes

- `global.json` pins SDK `10.0.400` with `rollForward: disable`; only `10.0.401` (and 9.0.319) is installed on this machine.
  To build locally, `rollForward` was set to `latestPatch` **in the working tree only** (not committed). CI is unaffected.
- A stale `.git/index.lock` (dated 2026-08-04, no git process running) was removed before the snapshot.
- Excluded from the snapshot (editor/tool junk, not project content): `scripts/__pycache__/*.pyc` (new files),
  `tmp-vendors.html` (empty), and draw.io temp/backup files `docs/thesis_diagrams/source/.$*.bkp|.dtmp`.

## Build — `dotnet build HRManagement.slnx`

**Result:** 3 errors, 10 warnings (pre-existing, unrelated to recruitment; not fixed per instructions).

| Project | Error |
|---------|-------|
| `HRManagement.AcceptanceTests` | `AcEst14JobCatalogCodPreviewTests.cs(30,35)`: CS1061 `HttpClient.PostAsJsonAsync` not found (missing `System.Net.Http.Json` using/reference) |
| `HRManagement.AcceptanceTests` | `AcEst15JobCatalogBulkImportTests.cs(49,41)`: same |
| `HRManagement.AcceptanceTests` | `AcEst17JobCatalogMigrationLegacyTests.cs(45,41)`: same |

All service projects, `HRManagement.UnitTests` and `HRManagement.IntegrationTests` build with 0 errors.

## Unit tests

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Recruitment|FullyQualifiedName~Hcm|FullyQualifiedName~SelectionWorkflow|FullyQualifiedName~RequisitionWorkflow|FullyQualifiedName~OfferWorkflow|FullyQualifiedName~Est0|FullyQualifiedName~Wf02"
```

**Result:** Passed 232, Failed 0, Skipped 0 (Total 232).

No failing tests at baseline.

## Verification of the plan's «الوضع الحالي» table (criteria / score / interview rows)

| Row | Plan claim | Code today | Match |
|-----|-----------|------------|-------|
| إنشاء معايير التقييم | only in `AcceptanceDataSeeder.cs:637`; `SelectionWorkflowRules.cs:57-62` requires `CriteriaVersionId` | Confirmed: the only writer is `AcceptanceDataSeeder.SeedSelectionPipelineAsync` (line 637). Gate is at `SelectionWorkflowRules.cs:57-63` (score null check + `CriteriaVersionId` null check). `DemoDataSeeder` creates no criteria at all. | Yes |
| الدرجة | single free number 0–100 (`RecordAssessment`), `RecruitmentSelectionService.cs:37-67` | Confirmed. Note: the 0–100 range is only enforced by `RecordAssessmentForm` `[Range]`; the service accepts any decimal. Saving also auto-moves `InterviewScheduled → Assessed`. | Yes (+ note) |
| المقابلة | `ScheduleAsync` only; regresses `Assessed/Ranked → InterviewScheduled` at `RecruitmentInterviewService.cs:32-38` | Confirmed. No past-date check, no overlap check, no panel, no cancel/complete. `/careers` TrackResult lists every slot regardless of status (all slots are `Scheduled` today). | Yes |

Other rows (session 2 scope) spot-checked: `RequisitionWorkflowRules.ActiveStatuses` = Draft/PendingApproval/Approved (no Published) — confirmed;
`CanCancel` at line 81 — confirmed.
