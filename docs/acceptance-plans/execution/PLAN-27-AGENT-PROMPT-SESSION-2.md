# Task (Session 2 of 2): PLAN-27 Part 2 — Offer terms consistency (Payroll grade lookup), reserve list, acceptance tests, docs

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). Work autonomously and follow the rules below strictly.

**This is session 2 of 2 for PLAN-27.**

- **Session 1** (branch `feature/plan-27-offer-merit-reserve`) implemented:
  - offer eligibility by merit
  - the exception (override) approval workflow with distinct approvers
  - offer outcomes (decline/revoke/expire)
  - the new statuses `OfferDeclined`/`ReserveListed`
  - the offer-expiry job
- **This session implements:**
  - **E** — offer terms consistency (grade from the funded position, salary bounds from Payroll through a new service-to-service lookup)
  - **F** — reserve list (next-candidate suggestion, reserve on hire, reserve expiry job)
  - **G** — acceptance tests for the whole of PLAN-27, plus remaining unit/integration tests
  - **H** — script and docs
- Do not redo session 1 work. Fix it only if a test proves a defect, and report it.

## Read first (mandatory)

1. **`reports/plan27-session1-handoff.md`** — the source of truth for session 1.
2. `docs/acceptance-plans/PLAN-27-hcm-offer-merit-reserve.md` (Arabic). This prompt refines it. **Where they differ, this prompt wins.**
3. `docs/HCM_001_GAP_STUDY.md` — G-06, G-07, G-20 — and decision D-7 (reserve validity).
4. Read these fully before changing them:
   - HR.Core: `RecruitmentOfferService`, `RecruitmentHireClosureService` (`RejectSiblingApplicationsAsync`), `RecruitmentRequisitionCascade`, `RecruitmentOptions`, the job classes from PLAN-26 and session 1, `Views/RecruitmentApplications/Details.cshtml`, `Views/OfferLetters/Details.cshtml`, `Views/RecruitmentRequisitions/Details.cshtml`, `ViewModels/RecruitmentSelectionViewModels.cs`
   - Payroll: `Payroll.Domain/Entities/{SalaryScale,PayGrade}.cs` (`PayGrade` has `GradeCode`, `BaseSalary`, `MinSalary`, `MaxSalary`; `SalaryScale` has `EffectiveFrom/EffectiveTo/IsActive`; both carry `TenantId`), `Payroll.Web/Controllers/DsarInternalController.cs` (internal S2S controller pattern), `Payroll.Web/Program.cs`
   - Shared S2S plumbing:
     - `src/Shared/HRManagement.ServiceDefaults/ServiceIdentity.cs` — `ServiceScopes`, `ServicePolicies`, `ServiceClientIds`; HR.Core's client id is `svc-hr-core`
     - `ServiceAuthorizationExtensions.cs` — e.g. the `DsarSubjectRead` policy with `IsAllowedClient`
     - `Integration/HrCoreLookupClient.cs` — `AddServiceAuthenticatedClient` pattern
     - `CodeGeneration/CodeGenerationClient.cs`
   - Keycloak: `scripts/sync-keycloak-dev-realm.ps1` and the realm JSON files under `docker/keycloak/` (grep `dsar.subject.read` to see how a scope is wired)
   - Patterns: HR.Core `Services/Establishment/FileFinanceBudgetPort.cs` (file-adapter fallback), `src/Tests/HRManagement.AcceptanceTests/AcHcm22RecruitmentWorkflowsUiTests.cs`, `scripts/hcm022-validate.ps1`

## Context and scope boundaries

- **PLAN-25 is deferred.** No changes to `WorkflowStageGuard`/`WorkflowEngine`/approval gates.
- **Out of scope:**
  - candidate self-service and notifications (PLAN-30 — markers only)
  - committee/appeal (PLAN-28)
  - hire→contract terms and non-commencement (PLAN-29)
  - multi-vacancy competitions (PLAN-31)
- Keep diffs minimal and follow existing style; Arabic user-facing messages; nonce CSP (no inline handlers or styles).
- **Never commit:**
  - `global.json` (local change)
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets
  - test-rewritten `testdata/*/out/*.json` (`git checkout -- testdata`)

## Verified repository facts

- **Migrations:**
  - HR.Core: `dotnet ef migrations add HCM027_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web` (after session 1's migration).
  - Payroll needs **no** schema change for this plan.
  - `has-pending-model-changes` must report none for HR.Core and Payroll.
- `RecruitmentApplication.Status` is stored as int. **Append enum members at the end only** (none are expected in this session).
- **Jobs:** `BackgroundService` + `AdvisoryLeaderElection.TryRunAsLeaderAsync` + `IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)`, unique lock key, disabled in test hosts, options under `RecruitmentOptions.Jobs`.
- Unit tests use SQLite in-memory contexts; integration tests use Testcontainers PostgreSQL; acceptance tests need `pwsh scripts/acceptance-up.ps1` + `RUN_ACCEPTANCE=true`.
- The acceptance project compiles only if the PLAN-26 wrap-up fixed the `System.Net.Http.Json` usings. If it does not compile, apply that fix first and report it.

## Stop and ask if

- a migration would rewrite business data;
- the S2S tenant propagation pattern is unclear or unsafe (do not invent a tenant bypass);
- a public message contract in `src/Shared/HRManagement.MessageContracts` must change;
- the branch state does not match the handoff.

---

## Step 0 — Verify session 1 state

1. Check out `feature/plan-27-offer-merit-reserve`. It must be clean (apart from local noise), and HEAD must equal the handoff SHA or be a descendant of it.
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm027|Hcm02|Recruitment|Offer|SelectionWorkflow"
   ```

   It must match the handoff. Otherwise stop and report.

---

## Workstream E — Offer terms consistency

**E1. Grade from the funded position**

- `CreateDraftAsync` pre-fills `GradeCode` from `FundedPosition.GradeCode`.
- A different grade requires `GradeDeviationReason` (≥ 10 chars) and turns the offer into an **exception offer**: it goes through `RECRUITMENT-OFFER-OVERRIDE-APPROVAL`, the same workflow session 1 uses for merit overrides.
- Generalise the trigger: `IsExceptionOffer = IsMeritOverride || GradeDeviation || SalaryUnverified`. Store it as a computed property or a flag, keep the existing `IsMeritOverride`, and update the policy fact and static fallback accordingly.

**E2. Payroll grade lookup** (new S2S read)

*Payroll side:*

- New internal controller `PayGradeLookupInternalController`:
  - route `api/internal/pay-grades`
  - `[Authorize(Policy = ServicePolicies.PayrollGradeLookup)]`
  - `GET {gradeCode}?asOf=yyyy-MM-dd` → `{ gradeCode, gradeNameAr, minSalary, maxSalary, baseSalary, salaryScaleCode, scaleEffectiveFrom, scaleEffectiveTo }` from the active `SalaryScale` effective at `asOf`
  - 404 when none
  - **Follow exactly how `DsarInternalController` resolves the caller's tenant.** Do not bypass tenant filters.

*Shared:*

- `ServiceScopes.PayrollGradesRead = "payroll.grades.read"`.
- `ServicePolicies.PayrollGradeLookup = "Service.PayrollGradeLookup"`: service account + scope + `IsAllowedClient(ServiceClientIds.HrCore)`, registered like `DsarSubjectRead`.

*Keycloak:*

- Add the client scope and grant it to `svc-hr-core` wherever `dsar.subject.read` is defined: `scripts/sync-keycloak-dev-realm.ps1` and the realm JSONs.
- Document it in `docs/SERVICE_TRUST_INVENTORY.md`.

*HR.Core side:*

- `IPayrollGradeLookup` → `Task<PayrollGradeLookupResult> GetAsync(string gradeCode, DateOnly asOf, CancellationToken)`, with result kinds `Found(min, max, base, nameAr)`, `NotFound`, `Unavailable(reason)`.
- `HttpPayrollGradeLookup`:
  - uses `AddServiceAuthenticatedClient` with base URL `Integration:PayrollBaseUrl`
  - timeout 5 s
  - any transport error or 5xx → `Unavailable`
- `UnavailablePayrollGradeLookup` (always `Unavailable("Payroll lookup not configured")`) is registered when `Integration:PayrollBaseUrl` is empty.
- Add `Integration:PayrollBaseUrl` to HR.Core's docker-compose/appsettings following how `Integration:HrCoreBaseUrl` is set for other services (development/acceptance compose only).

**E3. Salary validation in `CreateDraftAsync`** (`asOf = ProposedStartDate`):

| Lookup result | Outcome |
|---------------|---------|
| `Found` and salary within `[Min, Max]` | OK |
| `Found` and salary outside the range | reject: «الراتب المعروض خارج حدود الدرجة {grade}: {min}–{max}.» |
| `NotFound` | reject: «الدرجة {grade} غير معرّفة في سلّم الرواتب الساري.» |
| `Unavailable` | allow only with `SalaryJustification` (≥ 20 chars); set `SalaryUnverified = true` (an exception offer); show a warning banner |

- Store snapshots on the offer: `GradeMinSalary`, `GradeMaxSalary`, `SalaryCheckedAt`.
- Migration `HCM027_OfferTerms` adds: `GradeDeviationReason` (500), `SalaryUnverified` (bool), `SalaryJustification` (1000), `GradeMinSalary`/`GradeMaxSalary` (numeric 18,2, nullable), `SalaryCheckedAt`.

**E4. Dates**

- `ProposedStartDate` ≥ today (UTC) and ≤ today + `RecruitmentOptions.MaxStartDays` (default 90).

**E5. UI**

- The offer form shows the grade pre-filled from the position, and the salary range hint when the lookup succeeds (call it server-side when rendering the form).
- Deviation, justification and exception badges on `OfferLetters/Details`.

---

## Workstream F — Reserve list

**F1. `ReserveListService`** (HR.Core.Infrastructure/Services/Recruitment):

- `Task<RecruitmentApplication?> GetNextCandidateAsync(Guid requisitionId)` — the best available rank (reuse session 1's `GetBestAvailableRankAsync` and its definition of "available").
- `Task<int> MoveSiblingsToReserveAsync(Guid requisitionId, Guid hiredApplicationId, DateTime nowUtc)`.
- `Task<int> ExpireReserveAsync(DateOnly todayUtc)`.

**F2. Hire closure**

- Change `RecruitmentHireClosureService.RejectSiblingApplicationsAsync`:
  - siblings with status `Ranked` → `ReserveListed`, with `ReserveUntil = today + RecruitmentOptions.ReserveValidityMonths` (default 6) and `ReserveListedAt`
  - all other non-terminal siblings → `Rejected` (as today)
  - respect `RejectSiblingApplicationsOnHire = false` (no change at all)
- Migration `HCM027_ReserveList`: `ReserveUntil` (DateOnly?) and `ReserveListedAt` (DateTime?) on `RecruitmentApplication`.
- Add a unit test proving that the existing hire-closure integration behaviour is unchanged when the option is off.

**F3. Next-candidate banner**

- After a decline/expiry/revoke (application now `OfferDeclined`/`Rejected`) on a requisition that is still `Published`, show «المرشح التالي بالجدارة: {name} (رتبة {n}) — إنشاء عرض» on `RecruitmentRequisitions/Details` and `OfferLetters/Details`, linking to that application's offer form.
- Option `RecruitmentOptions.AutoDraftNextOffer` (default `false`). When true, create the draft automatically through `CreateDraftAsync`, copying grade/salary/validity from the previous offer, and run the same eligibility and terms checks.

**F4. Reserve expiry job**

- Worker `RecruitmentReserveExpirer` + `RecruitmentReserveExpiryJob` (daily): `ReserveListed` with `ReserveUntil < today` → `Rejected` with note «انتهاء صلاحية قائمة الاحتياط».
- Options `Jobs.ReserveExpiryEnabled` (true) and `Jobs.ReserveExpiryIntervalMinutes` (1440).
- Leave `// PLAN-29: recall from reserve on non-commencement` where re-use would happen, and `// PLAN-30: notify` after the status change.

**F5. UI**

- Show reserve status and «حتى {date}» on application lists/Details and on the requisition rank board.

---

## Workstream G — Tests

### Unit (SQLite)

**`Hcm027OfferTermsTests`** (with a fake `IPayrollGradeLookup`)
- grade pre-filled
- grade deviation without a reason rejected; with a reason → exception workflow
- salary out of range rejected
- `NotFound` rejected
- `Unavailable` without justification rejected; with justification → `SalaryUnverified` + exception workflow
- start-date bounds

**`Hcm027ReserveListTests`**
- hire moves `Ranked` siblings to `ReserveListed` with `ReserveUntil`, and others to `Rejected`
- option off → unchanged behaviour
- next candidate skips `OfferDeclined`/`Rejected`/pending-appeal applications
- `AutoDraftNextOffer` creates a draft that passes eligibility
- reserve expiry is idempotent

**`HttpPayrollGradeLookupTests`** (fake `HttpMessageHandler`)
- 200 → `Found`
- 404 → `NotFound`
- 500/timeout → `Unavailable`

### Integration (Testcontainers)

**`Hcm027PayGradeLookupIntegrationTests`** against the Payroll `DbContext`/controller
- returns the active scale at `asOf`
- respects the tenant
- 404 for an unknown grade
- an unauthorised caller (no scope / wrong client) → 403

Use `Skip.If` when Docker is unavailable.

### Acceptance — whole PLAN-27 (`src/Tests/HRManagement.AcceptanceTests/AcHcm27OfferMeritTests.cs`)

- Cover **AC-HCM-27a..h** from the plan with `EvidenceRecorder("AC-HCM-27", correlationId)`.
- Personas: `central-a` (maker), `br1-mgr` (step 1), `a-management-approver` and `a-management-approver-2` (steps 2/3).
- Use `Skip.If` for missing preconditions — **never** `WriteResult("Skip"); return;`.
- Prefer deterministic seeded data. Extend `AcceptanceDataSeeder` with a requisition having 3 ranked candidates if needed (deterministic, idempotent).
- If the local stack is available, run it and produce `acceptance-evidence/AC-HCM-27/`; otherwise report that it was not run.

### Regression

Keep green, or matching the baseline: `Hcm026`, `Hcm027`, `Hcm021`, `Hcm022`, `RecruitmentHireClosure`, `Fun001`, `Syr001`, plus the Payroll tests touching pay grades. List every modified existing test with a justification.

---

## Workstream H — Script and docs (cover both sessions)

- `scripts/hcm027-validate.ps1`, modelled on `scripts/hcm026-validate.ps1`/`hcm022-validate.ps1`: checks that the files and the `HCM027_*` migrations exist, then runs unit tests filtered by `Hcm027` plus the `Hcm026|Hcm022` regression.
- `docs/acceptance-plans/execution/PLAN-27-EXECUTION.md`: prerequisites (including the new persona env var and the Keycloak scope sync), commands, personas, Arabic manual steps, evidence folder.
- `PLAN-27-hcm-offer-merit-reserve.md`:
  - Status → `Implemented (acceptance evidence pending)` or `Implemented`
  - "Implementation notes / deviations" for both sessions — notably: step 3 of the exception workflow uses `hr-management-approver` with distinct approvers instead of `compliance` (PLAN-25 deferred), and the exception trigger was generalised to grade deviation and unverified salary
- `docs/HCM_001_RECRUITMENT.md`: the updated application/offer state machines (new statuses), merit rule, exception offers, offer expiry, reserve list. Mark G-06/G-07 as addressed.
- `docs/HIRE_ONBOARDING_GUIDE.md`: the offer steps (validity days, merit, exception approval, decline/revoke, next candidate, reserve).
- `docs/MESSAGE_EVENT_MATRIX.md` only if any event changed (none expected).
- `docs/SERVICE_TRUST_INVENTORY.md` (new scope).
- `docs/DATABASE_SCHEMA_INVENTORY.*` (generator if one exists).

---

## Commit plan

6. `feat(hcm027): offer terms — grade from position, Payroll grade lookup (S2S), salary bounds`
7. `feat(hcm027): reserve list — siblings to reserve on hire, next candidate, reserve expiry job`
8. `test(hcm027): integration + acceptance AC-HCM-27a..h`
9. `docs(hcm027): plan status, execution, guides, trust inventory, validate script`

Every commit builds; tests green at 6, 7 and 9.

## Definition of done (whole PLAN-27)

- Offers can only go to ranked candidates in merit order, unless an exception is documented and approved by 3 distinct approvers.
- Grade comes from the funded position and the salary is within the Payroll grade bounds (or explicitly flagged as unverified with a justification and an exception approval).
- Decline/revoke/expiry settle the application; expiry is automatic; the next candidate by merit is suggested.
- On hire, ranked siblings move to a reserve list that expires automatically.
- Build has 0 errors; `has-pending-model-changes` reports none for HR.Core and Payroll; unit/integration tests pass; `scripts/hcm027-validate.ps1` passes.
- Acceptance tests are written, and run with evidence or explicitly reported as not run.

## Final report (English; quote Arabic UI strings as-is; cover both sessions)

1. Summary per workstream (A–D from the handoff, verified; E–H).
2. Commits of both sessions (sha + message).
3. Migrations (HR.Core) and the Keycloak/scope changes.
4. Test results: baseline vs. final, new classes, modified tests (with justification).
5. Acceptance: run or not (why), evidence path.
6. Deviations and judgement calls (especially S2S tenant handling).
7. Open items and `// PLAN-2x:` markers (file:line).
8. Manual test script (Arabic UI paths): merit offer, exception offer with 3 approvers, out-of-range salary, decline → next candidate, expiry, hire → reserve list.
9. `git log --oneline` of the branch, so the next plan can branch from it.
