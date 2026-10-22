# Task (Session 1 of 2): PLAN-27 Part 1 — Offer eligibility by merit, exception approval, offer outcomes and expiry

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). Work autonomously and follow the rules below strictly.

**This is session 1 of 2 for PLAN-27.**

- **This session implements:**
  - Step 0 (baseline)
  - Workstream **A** — offer eligibility and merit order
  - Workstream **B** — exception approval workflow
  - Workstream **C** — offer outcomes: decline, revoke, expiry, new application statuses
  - Workstream **D** — offer expiry job
  - unit tests for the above
- **Session 2 will implement:** offer terms consistency (grade + salary via a Payroll lookup), the reserve list and reserve expiry, acceptance tests and docs. **Do not implement those here.**
- At the end, write a handoff file for session 2.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-27-hcm-offer-merit-reserve.md` — the plan (Arabic). This prompt refines it. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md` — gaps **G-06** and **G-07**.
3. `reports/plan26-session1-handoff.md` — PLAN-26 conventions you must reuse (`RecruitmentOptions`, ctor signatures, test patterns). Also read the PLAN-26 session 2 commit bodies: `git log --format='%h %s%n%b' 544ba7a..HEAD`.
4. Read these fully before changing them:
   - `src/Services/HRCore/HR.Core.Domain/Entities/{OfferLetter,RecruitmentApplication}.cs`
   - `src/Services/HRCore/HR.Core.Domain/Enums/{OfferStatus,RecruitmentApplicationStatus}.cs`
   - `src/Services/HRCore/HR.Core.Domain/Services/{OfferWorkflowRules,SelectionWorkflowRules,RecruitmentAppealWorkflowRules}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/{RecruitmentOfferService,OfferLetterWorkflowHandler,RecruitmentRequisitionCascade,RecruitmentRequisitionLifecycleService,RecruitmentHireClosureService,RecruitmentOptions}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs` (`ValidateSelfApprovalAsync` only)
   - `src/Services/HRCore/HR.Core.Infrastructure/Data/{WorkflowPackSeeder,PolicyEngineDomainSeeds}.cs` and `Services/Policy/PolicyStaticFallback.cs`
   - `PolicyDomainFactsBuilders.ForOfferLetter` (grep for it)
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/LeaveScheduleReconcilerJob.cs` and the PLAN-26 `RecruitmentIntakeCloseJob` (job pattern)
   - `src/Services/HRCore/HR.Core.Web/Controllers/{RecruitmentApplicationsController,OfferLettersController}.cs`, `ViewModels/RecruitmentSelectionViewModels.cs` (`CreateOfferForm`), `Views/RecruitmentApplications/Details.cshtml`, `Views/OfferLetters/{Details,Index}.cshtml`, `Services/OfferLetterWorkflowUiService.cs`
   - `testdata/acceptance/personas.json` and `scripts/prepare-keycloak-import.ps1`

## Context and scope boundaries

- **PLAN-25 is deferred** (no server yet). Do not change `WorkflowStageGuard`/`WorkflowEngine`. In `WorkflowInstanceActionService` only extend `ValidateSelfApprovalAsync` as described in B3. `compliance` cannot reach HR.Core yet, so **no workflow step may require the `compliance` role**.
- **Out of scope:**
  - offer grade/salary validation, Payroll lookup, reserve list (session 2)
  - candidate self-service accept/decline and notifications (PLAN-30 — leave `// PLAN-30:` markers)
  - committee/appeal (PLAN-28)
  - hire/contract (PLAN-29)
- Keep diffs minimal. Follow the existing style:
  - primary constructors
  - `(bool Success, string? Error)` tuples
  - `TempData` + redirect
  - Arabic user-facing messages
- **Never commit:**
  - `global.json` (local change)
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets
  - `testdata/*/out/*.json` rewritten by test runs — `git checkout -- testdata` before committing

## Verified repository facts

- `RecruitmentApplication.Status` and `InterviewSlotStatus` are stored as **int**; `RecruitmentRequisition.Status` as **string**; `OfferStatus` — check its mapping. **Append new enum members at the end only.**
- **Migrations:**
  - Command: `dotnet ef migrations add HCM027_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web`
  - Must be generated after the last `HCM026_*` migration.
  - Guard Postgres-only SQL with `migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"`.
  - `has-pending-model-changes` must report none.
- `RecruitmentOptions` (HR.Core.Infrastructure/Services/Recruitment) already exists, with a nested `Jobs` object. **Extend it; do not create a new options class.**
- **Jobs:** `BackgroundService` + `AdvisoryLeaderElection.TryRunAsLeaderAsync` + `IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)`. Use a unique advisory-lock key, and disable the job in test hosts.
- **Policies:** `HrCoreReadOnly`, `HrCoreWrite`, `HrCoreApprove`, `HrCoreBranchOperation`, `HrCoreDelete`. CSP is nonce-based: no inline handlers and no inline `style=""` in new markup.
- Unit tests use SQLite in-memory `HRCoreDbContext`.
- `RecruitmentRequisitionCascade` (PLAN-26) owns the "open/non-terminal application" sets used by cancel/close. It has a `// PLAN-27:` marker in `RecruitmentRequisitionLifecycleService.CloseAsync` (~line 288).

## Stop and ask if

- a migration would rewrite existing business data;
- a public message contract in `src/Shared/HRManagement.MessageContracts` must change;
- policy-engine priority semantics make B4 impossible without changing the engine;
- the code differs materially from what is described here.

---

## Step 0 — Baseline

1. Start from the **tip of `feature/plan-26-assessment-interview-requisition`** (PLAN-26 is not merged to `main`). Create branch `feature/plan-27-offer-merit-reserve`.
2. `git status` must be clean apart from the local noise above.
3. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm02|Recruitment|Offer|SelectionWorkflow|RequisitionWorkflow|Fun001|Syr001"
   ```

   Record the results in `reports/plan27-baseline.md`. Do not fix unrelated failures.

---

## Workstream A — Offer eligibility and merit order (G-07)

**Problems today:**

- `RecruitmentOfferService.CreateDraftAsync` checks neither the application status nor the rank.
- `Views/RecruitmentApplications/Details.cshtml` (~line 273) shows the offer form for `Assessed` as well as `Ranked`.
- An `Assessed` candidate who receives an offer stays `Assessed`, and `AcceptAsync` then jumps straight to `OfferAccepted`, bypassing ranking.

**A1. Domain `OfferEligibilityRules`** (`HR.Core.Domain/Services/`) — pure, unit-testable:

- `string? Validate(RecruitmentApplication app, int? bestAvailableRank, bool requisitionHasOtherActiveOffer, bool isMeritOverride, string? meritOverrideReason)`. It rejects when:
  - status ≠ `Ranked` (or `ReserveListed`, added in C1) → «يمكن إصدار العرض للمرشحين المرتبين فقط.»
  - a pending appeal exists (reuse `RecruitmentAppealWorkflowRules.CanOffer`)
  - `requisitionHasOtherActiveOffer` → «يوجد عرض نشط على هذا الشاغر.»
  - `app.Rank != bestAvailableRank` and `!isMeritOverride` → «المرشح ليس الأعلى رتبة المتاح (الرتبة المتاحة: {n}).»
  - `isMeritOverride` and the reason is shorter than 30 characters → Arabic message
- **"Available"** = applications on the same requisition with status `Ranked` or `ReserveListed`, `Rank != null`, and no pending appeal. `bestAvailableRank` = the minimum `Rank` among them.
- **"Other active offer"** on the requisition = any offer of another application on the same requisition with status Draft/PendingApproval/Issued, or Accepted with `!IsConvertedToEmployee`.

**A2. Service**

- `RecruitmentOfferService.CreateDraftAsync` gains `bool isMeritOverride` and `string? meritOverrideReason`, computes the two inputs with queries, and calls `OfferEligibilityRules.Validate`.
- Add a public query `GetBestAvailableRankAsync(Guid requisitionId)` (session 2's reserve list will reuse it).
- Keep the existing legal-source checks.
- `Ranked → OfferExtended` stays.

**A3. UI**

- Remove `Assessed` from the offer-form condition in `Details.cshtml`.
- On the rank board, mark the «التالي بالجدارة» row.
- Show the «تجاوز الجدارة» checkbox and reason textarea only when the current application is not the best available rank (JS file or server-side flag; no inline handlers).

---

## Workstream B — Exception approval workflow

**B1. Data.** Migration `HCM027_OfferOutcome` (shared with C) adds to `OfferLetter`:

- `IsMeritOverride` (bool)
- `MeritOverrideReason` (string? 1000)

**B2. Workflow definition.** In `WorkflowPackSeeder`, add `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` (entity type `OfferLetter`), with a new deterministic Guid following the existing numbering. Three steps:

1. «مراجعة HR» — `hr-core`
2. «اعتماد إداري» — `hr-management-approver`
3. «اعتماد استثناء الجدارة» — `hr-management-approver`

> `compliance` is intentionally not used until PLAN-25 lands. Leave `// PLAN-25: move step 3 to compliance` next to the definition.

**B3. Distinct approvers.** Extend `WorkflowInstanceActionService.ValidateSelfApprovalAsync` for `OfferLetter`:

- When the instance's definition code is `RECRUITMENT-OFFER-OVERRIDE-APPROVAL`, an actor who already approved **any earlier step** of the same instance (read `StepHistoryJson`) cannot approve a later step: «لا يجوز لنفس المعتمد اعتماد أكثر من خطوة في عرض استثنائي.»
- Keep the existing creator SoD.
- Do not change anything else in that service.

**B4. Starting the right workflow**

- `RecruitmentOfferService.SubmitForApprovalAsync` uses `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` when `offer.IsMeritOverride`, both in the `Off` + lab-scale path and in policy mode.
- For policy mode:
  - add the fact `isMeritOverride` to `PolicyDomainFactsBuilders.ForOfferLetter`
  - add `EnsureFactsFieldAsync` for it
  - add a rule `R-OFF-OVERRIDE` (TriggerWorkflow `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` when `action == Issue && isMeritOverride == true`) in `PolicyEngineDomainSeeds`
  - mirror it in `PolicyStaticFallback`
- **Verify the engine's rule-priority semantics**, so that the override rule wins over `R-OFF-ISSUE` for override offers and does not fire for normal ones. Add a test for both.

**B5. Persona for acceptance.**

- Add a second tenant-A management approver `a-management-approver-2` (role `hr-management-approver`) to `testdata/acceptance/personas.json`, following the existing entries (new `passwordEnv` name; **no password values committed**).
- Make sure `scripts/prepare-keycloak-import.ps1` picks it up (adjust only if needed).
- Document the new env var where the other persona env vars are documented.

**B6. UI**

- `OfferLetters/Details`: an «عرض استثنائي — تجاوز الجدارة» badge plus the reason.
- `OfferLetters/Index`: a filter for exception offers.
- The workflow panel already renders any definition — verify it shows 3 steps.

---

## Workstream C — Offer outcomes (G-06)

**C1. New application statuses** (append to `RecruitmentApplicationStatus`, with Arabic `[Display]`):

| Value | Arabic name |
|-------|-------------|
| `OfferDeclined = 11` | «عرض مرفوض/منتهٍ» |
| `ReserveListed = 12` | «قائمة احتياط» |

Session 1 only adds `ReserveListed` and its transitions; **session 2** assigns it.

**C2. Transitions in `SelectionWorkflowRules`:**

- `OfferExtended → OfferAccepted | OfferDeclined | Rejected | Withdrawn`
- `Ranked → OfferExtended | ReserveListed | Rejected | Withdrawn` (keep the existing ones)
- `ReserveListed → OfferExtended | Rejected | Withdrawn`
- `OfferDeclined` is **terminal**.
- **Neither new status is a manual target:** add both to `SystemOnlyTargets`, so `GetAllowedTargets` never offers them.
- In `RecruitmentRequisitionCascade`, treat `OfferDeclined` as terminal and `ReserveListed` as open (rejected on cancel/close). Replace the `// PLAN-27:` marker in `CloseAsync` with a short comment explaining that.

**C3. Offer data** (same migration `HCM027_OfferOutcome`):

- `DeclineReason` (1000), `DeclinedAt`, `DeclinedBy` (string 20: `Candidate` | `HrOnBehalf`)
- `RevokeReason` (1000), `RevokedAt`
- `ExpiredAt`
- `ValidityDays` (int, default from options)

**C4. Service changes in `RecruitmentOfferService`:**

| Method | Rules | Effect on the offer | Effect on the application |
|--------|-------|---------------------|---------------------------|
| `DeclineAsync(offerId, reason, declinedBy)` | reason ≥ 5 chars | `Declined` + fields | `OfferDeclined`; `ActiveOfferLetterId = null` (the offer stays in the `OfferLetters` collection as history); `EntityAuditLog` |
| `RevokeAsync(offerId, reason, revertToRanked)` | reason ≥ 10 chars | `Revoked` + fields; an `InProgress` workflow instance → `WorkflowStatus.Cancelled` | `revertToRanked` → `Ranked` (administrative error); otherwise `Rejected` with a note. `ActiveOfferLetterId = null`; audit |
| `ExpireAsync(offerId, nowUtc)` | only `Issued` with `ExpiryDate < nowUtc` | `Expired` + `ExpiredAt` | `OfferDeclined` with note «انتهاء صلاحية العرض»; `ActiveOfferLetterId = null` |

**Issue date and expiry:**

- `ExpiryDate` is computed **when the offer becomes `Issued`**, not at draft time: `IssuedDate.Date + ValidityDays` (end of day UTC).
- Apply this in `OfferLetterWorkflowHandler` (approved branch) and in any other path that sets `Issued`.
- Add `RecruitmentOptions.OfferValidityDays` (default 14; allowed range 3..60, validated in the service).
- Leave `// PLAN-30: notify candidate` after decline, revoke and expiry.

**C5. UI**

- `CreateOfferForm` and the form in `Details.cshtml`: replace the free `ExpiryDate` input with «مدة صلاحية العرض (أيام)», defaulting to the option value.
- `OfferLetters/Details`:
  - **decline:** reason + who declined. For now only «HR نيابة عن المرشح» is offered in the UI; `Candidate` is reserved for PLAN-30.
  - **revoke:** reason + checkbox «خطأ إداري — إعادة المرشح إلى المرتبين».
  - Show the outcome fields.
- `RecruitmentApplications/Details`: show the offer history (all `OfferLetters` of the application with status and reasons).

---

## Workstream D — Offer expiry job

- Worker `RecruitmentOfferExpirer.ExpireDueAsync(DateTime nowUtc, CancellationToken)`:
  - finds `Issued` offers with `ExpiryDate < nowUtc` across tenants and calls the same logic as `ExpireAsync`
  - idempotent; returns a count
- `RecruitmentOfferExpiryJob : BackgroundService`, following the job pattern:
  - add `RecruitmentOptions.Jobs.OfferExpiryEnabled` (true) and `Jobs.OfferExpiryIntervalMinutes` (60)
  - register it in `Program.cs` next to the intake-close job
  - disable it in test hosts
- Leave `// PLAN-30: reminder 48h before expiry` in the worker.

---

## Tests (unit, SQLite pattern)

**`Hcm027OfferEligibilityRulesTests`** (pure)
- Assessed rejected
- Ranked best accepted
- rank 2 without override rejected
- rank 2 with override and a short reason rejected
- rank 2 with override and a valid reason accepted
- pending appeal rejected
- other active offer rejected

**`Hcm027OfferServiceEligibilityTests`**
- `GetBestAvailableRankAsync` ignores Rejected/OfferDeclined/pending-appeal applications
- second parallel offer on the same requisition rejected
- override flag persisted

**`Hcm027OverrideWorkflowTests`**
- an override offer starts `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` (lab path); a normal offer starts `RECRUITMENT-OFFER-APPROVAL`
- the same actor approving steps 2 and 3 is rejected
- the creator cannot approve (existing SoD)
- 3 distinct approvers → `Issued`
- policy-mode rule selection (static fallback and/or seeded rules)

**`Hcm027OfferOutcomeTests`**
- decline → application `OfferDeclined` and `ActiveOfferLetterId` cleared
- revoke with `revertToRanked` → `Ranked`; without it → `Rejected`
- revoking a PendingApproval offer cancels its workflow instance
- `ExpiryDate` is set at Issue from `ValidityDays`
- accepting after expiry is rejected
- `ExpireAsync` on a non-expired offer is rejected

**`Hcm027OfferExpirerTests`**
- expires due offers only
- idempotent
- application updated

**`SelectionWorkflowRules`**
- new transitions
- `GetAllowedTargets` never contains `OfferDeclined`/`ReserveListed`
- cascade sets (cancel/close) treat `ReserveListed` as open and `OfferDeclined` as terminal

**Regression** — green, or matching the baseline: `Hcm02`, `RecruitmentHireClosure`, `RecruitmentRequisition`, `OfferWorkflowRules`, `Syr001`, `Fun001`. List every modified existing test with a one-line justification.

---

## Commit plan

0. (none; branch from the PLAN-26 tip)
1. `feat(hcm027): offer eligibility rules + merit order`
2. `feat(hcm027): exception approval workflow (override, distinct approvers, persona)`
3. `feat(hcm027): offer outcomes — decline/revoke/expire, new application statuses`
4. `feat(hcm027): offer expiry job`
5. `docs(hcm027): session 1 handoff`

Every commit builds; unit tests green at 3, 4 and 5.

## Definition of done (session 1)

- No path reaches `OfferExtended`/`OfferAccepted` without `Ranked`.
- Skipping merit order requires a written reason and the 3-step workflow with distinct approvers.
- At most one active offer per requisition.
- Decline/revoke/expiry always settle the application, and expiry is automatic.
- Build has 0 errors in service and test projects; `has-pending-model-changes` reports none; new and regression unit tests pass.

## Handoff (mandatory last step)

Write and commit `reports/plan27-session1-handoff.md` with:

1. Branch, commits (sha + message).
2. The migration and its columns.
3. New and changed types and file paths.
4. `RecruitmentOptions` additions.
5. Public signatures session 2 will use:
   - `GetBestAvailableRankAsync`
   - `CreateDraftAsync` (new params)
   - `DeclineAsync`/`RevokeAsync`/`ExpireAsync`
   - `OfferEligibilityRules`
   - the status sets in `RecruitmentRequisitionCascade`
6. Workflow code and persona added (env var name).
7. Test results (baseline vs. now), modified tests.
8. Deviations, known issues, `// PLAN-2x:` markers (file:line).
9. Notes for session 2.

Then reply with a short summary.
