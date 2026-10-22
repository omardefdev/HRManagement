# Task (Session 2 of 2): PLAN-29 Part 2 — Offer terms into contract and payroll, appointment decision, medical step, commencement watch; acceptance; docs

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) / MassTransit engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). Work autonomously and follow the rules below strictly.

**This is session 2 of 2 for PLAN-29.**

- **Session 1** (branch `feature/plan-29-hire-contract-documents`) implemented the HR.Core side:
  - person matching and safe hire errors
  - employment type aligned with `EmploymentType`
  - `ICandidateAccepted` **v4** with the offer terms (and no hire event for internal mobility)
  - the offer letter PDF with a verification hash
- **This session implements:**
  - **E** — HR.Contracts lineage and contract terms
  - **F** — contract-type resolution with a configurable regime map and intervention instead of silent fallback
  - **G** — `IContractActivated` carries salary and grade
  - **H** — Payroll pay-profile fix
  - **I** — hire checklist: appointment decision and optional pre-employment medical
  - **J** — commencement watch (detection + intervention only)
  - **K** — acceptance tests for all of PLAN-29
  - **L** — script and docs
- Do not redo session 1 work. Fix it only if a test proves a defect, and report it.

## Read first (mandatory)

1. **`reports/plan29-session1-handoff.md`** — the source of truth for session 1, especially the exact v4 contract.
2. `docs/acceptance-plans/PLAN-29-hcm-hire-contract-documents.md` (Arabic), including the «تعديلات قبل التنفيذ» note. This prompt refines the plan. **Where they differ, this prompt wins.**
3. `docs/HCM_001_GAP_STUDY.md` — gaps **G-13** and **G-22** — and decision **D-6**.
4. Read these fully before changing them:
   - HR.Contracts:
     - `Infrastructure/Consumers/InboundEventConsumers.cs` (`CandidateAcceptedConsumer`)
     - `Application/Features/InboundEvents/InboundEventCommands.cs` (`ProcessCandidateAcceptedCommand` + handler; notes-based lineage `BuildHireNotes`/`ParseRecruitmentApplicationId`)
     - `Domain/Services/PendingContractProvisioning.cs`
     - `Domain/Entities/{Contract,ContractType,ServiceTransaction,HireChecklist,AdministrativeDecision,CommencementRecord}.cs`
     - `Domain/Enums/HireStep.cs` (stored as **string**)
     - `Domain/Services/{HireChecklistDefaults,HireWorkflowRules}.cs`
     - `Application/Features/HireCases/{HireCaseSupport,HireCaseHandlers}.cs`
     - `Infrastructure/Messaging/MassTransitEventPublisher.cs` (`PublishContractActivatedAsync` sends `BaseSalary/PayGradeCode` as null, ~lines 43–45)
     - `Infrastructure/Sagas/HireCaseStateMachine.cs`
     - `Infrastructure/Consumers/SagaSupportConsumers.cs` (`ProcessInterventionRequestedConsumer` → `ProcessInterventions` for `SagaTypeNames.Hire`)
     - `Infrastructure/Data/{ContractsDbContext,ContractsDataSeeder,AcceptanceContractsSeeder,DemoContractsSeeder}.cs` (`DEFAULT` contract type is seeded)
     - `Web/Controllers/{HireWizardController,AdministrativeDecisionsController,CommencementRecordsController,ContractTypesController}.cs` and the HireWizard views (`Step2`/`Step4`)
     - `ICodeGenerationClient` usage for reference numbers
   - Payroll:
     - `Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs` (`HandleContractActivatedHandler`: `BaseSalary = request.BaseSalary ?? grade?.BaseSalary ?? 0`; grade looked up by code only)
     - `Payroll.Domain/Entities/{SalaryScale,PayGrade,EmployeePayProfile,FraudAlert,PaymentHold}.cs`
   - Shared:
     - `MessageContracts/Events/{ICandidateAccepted,IContractActivated}.cs`
     - `MessageContracts/Commands/ProcessSagaCommands.cs` (`IProcessInterventionRequested { SagaType, CorrelationId, Reason, FailedParty, TenantId }`)
   - Reference: `docs/syr/service-regimes.json` (`law50`, `law53`, `special_banking`, `special_judiciary`)

## Context and scope boundaries

- **PLAN-25 is deferred.** No generic gate changes.
- **Out of scope:**
  - cancelling a hire for non-commencement and recalling from the reserve list (leave `// PLAN-29 follow-up:` markers; detection only in J)
  - File.Storage archiving
  - candidate portal/notifications (PLAN-30)
- **Do not seed new legal contract types into production data.** Mappings are configuration managed through a screen. Acceptance/demo seeders may add clearly labelled test types.
- Keep diffs minimal; Arabic user-facing messages; nonce CSP.
- **Never commit:**
  - `global.json` (local change)
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets
  - test-rewritten `testdata/*/out/*.json` (`git checkout -- testdata`)

## Verified repository facts

- **HR.Contracts migrations:**
  - Command: `dotnet ef migrations add FUN029_<Purpose> -p src/Services/HRContracts/HR.Contracts.Infrastructure -s src/Services/HRContracts/HR.Contracts.Web` (latest existing: `SYR009_SeparationRegimeBinding`).
  - Guard Postgres-only SQL with `migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"`.
  - `has-pending-model-changes` must report none for HR.Contracts, HR.Core and Payroll.
- `HireChecklist` has `IsCompleted`, `CompletedAt`, `Notes`, `OwnerRole`, `SlaHours`, `EvidenceRef`, `CompletedBy(Name)`.
- **Hire case gates:**
  - `HireWorkflowRules.CanAdvanceToCompleted` requires all checklist items.
  - `CanPublishHireCompleted` requires all except `HireArchived`.
  - `HireChecklistDefaults.DefaultSteps` = all enum values today.
- **HireWizard policies:** `HrContractsHireView` (class), `HrContractsHireExecute`, `HrManagementApprove` (Step 3), `HrContractsApprove` (Step 4).
- **Unit tests:** use the in-memory/SQLite patterns of the Contracts and Payroll test classes (grep `Fun001HireJourneyTests`, `HireWizardContinueTests`).

## Stop and ask if

- a migration would rewrite or delete business data (the lineage backfill only **fills new columns**);
- an existing Payroll behaviour change would break the FUN-001 hire journey tests in a way you cannot keep compatible;
- the branch state does not match the handoff.

---

## Step 0 — Verify session 1 state

1. `git checkout -- testdata`. Check out `feature/plan-29-hire-contract-documents`, at the handoff SHA or a descendant, clean apart from local noise.
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm029|Hcm02|Hire|Fun001|Payroll"
   ```

   It must match the handoff. Otherwise stop and report.

---

## Workstream E — Lineage and contract terms in HR.Contracts

**E1. Migration `FUN029_ContractOfferTerms`**

On `Contract`:
- `GradeCode` (50)
- `AgreedBaseSalary` (numeric 18,2, nullable)
- `LegalRegimeId` (50)
- `EmploymentType` (20)
- `SourceOfferLetterId` (Guid?)

On `ServiceTransaction`:
- `RecruitmentApplicationId`, `RequisitionId`, `OfferLetterId` (Guid?)
- `ExpectedStartDate` (DateOnly?)
- `AppointmentDecisionId` (Guid?)
- `CommencementOverdueAt` (DateTime?)
- index on `(TenantId, RecruitmentApplicationId)`

Postgres-guarded **backfill** of `RecruitmentApplicationId` from `Notes` (pattern `recruitmentApplicationId:<guid>`, as parsed by `ParseRecruitmentApplicationId`). It only fills the new column.

**E2. Handler**

`ProcessCandidateAcceptedCommand` gains the v4 fields. `CandidateAcceptedConsumer` maps them, with defaults for v3 messages.

`ProcessCandidateAcceptedHandler`:

1. **Defence in depth:** if `IsInternalMobility` → log and return. Session 1 already stopped publishing these.
2. Store the lineage columns and `ExpectedStartDate` on the transaction. Keep writing the notes text for readability, but **all reads use the columns** — replace `ParseRecruitmentApplicationId` call sites. Keep the method only for the backfill and its test.
3. Create the pending contract through an extended `PendingContractProvisioning.CreatePending(..., terms)` carrying `GradeCode`, `AgreedBaseSalary`, `LegalRegimeId`, `EmploymentType` and `SourceOfferLetterId`, using the contract type from F.
4. Keep idempotency exactly as today (no duplicate transaction, contract or checklist on redelivery). Add a test that redelivers the same message twice.

---

## Workstream F — Contract-type resolution (no silent fallback)

**F1. Entity `ContractTypeRegimeMap`** (Contracts domain, `AuditableEntity`, tenant-filtered):

- `LegalRegimeId` (string? 50) — null = any regime
- `EmploymentType` (string? 20) — null = any type
- `ContractTypeId` (FK)
- `IsActive`
- unique `(TenantId, LegalRegimeId, EmploymentType)` — handle nulls deliberately (e.g. a filtered index or a normalized `'*'` value; choose one and document it)

**F2. `ContractTypeResolver.ResolveAsync(legalRegimeId, employmentType)`** — most-specific match first:

1. `(regime, type)`
2. `(regime, *)`
3. `(*, type)`
4. `(*, *)`

If nothing matches: fall back to `DEFAULT` **only** when `Contracts:AllowDefaultContractTypeFallback` is true (default `true` in `appsettings.json`/Development; set `false` in `appsettings.Production.json` and `appsettings.Staging.json`). Otherwise the result is **unresolved**.

**F3. Unresolved**

- Still create the `Appointment` transaction and its checklist, but **no contract**.
- The `ContractClassified` item gets `Notes` «لا يوجد نوع عقد مطابق لنظام الخدمة {regime} / {type} — يتطلب تدخلاً».
- Publish `IProcessInterventionRequested { SagaType = SagaTypeNames.Hire, CorrelationId = transaction.Id, Reason = …, FailedParty = "HR.Contracts", TenantId }`, so it appears in the existing interventions list.
- Increment a meter, following the hire saga metrics style.
- `HireWizard/Step2` shows a red banner, plus a button for `HrContractsHireExecute` to **create the pending contract manually** with a chosen contract type. That button clears the banner and runs the normal flow.

**F4. Admin screen** `ContractTypeRegimeMapsController` (list/create/edit/deactivate; read `HrContractsHireView`, write `HrContractsApprove` — or the closest existing admin policy; report your choice).

**F5. Seeders**

- Production seeding: none beyond `DEFAULT`.
- `AcceptanceContractsSeeder`/`DemoContractsSeeder`: add labelled test contract types (e.g. `ACC-LAW50-PERM` «تجريبي — دائم وفق القانون 50») and mappings for `law50`/`Permanent` and `law50`/`Temporary`, deterministic and idempotent.

---

## Workstream G — `IContractActivated` carries salary and grade

- In `PublishContractActivatedAsync`: `BaseSalary = contract.AgreedBaseSalary` and `PayGradeCode = contract.GradeCode` instead of null.
- Contracts created outside recruitment (manual) keep null when unknown.
- If a `SchemaVersion` bump is not needed (fields already exist), do not bump. Explain.

---

## Workstream H — Payroll pay-profile fix

In `HandleContractActivatedHandler`:

- **Resolve the grade** by `GradeCode` **within the active `SalaryScale` effective at the contract `StartDate`** (`IsActive`, `EffectiveFrom <= start`, `EffectiveTo` null or `>= start`). Fall back to the current lookup only when no dated scale matches, and log a warning.
- **Salary** = `request.BaseSalary ?? grade?.BaseSalary`.
- **Out of range** (grade found and salary outside `[MinSalary, MaxSalary]`): still create the profile, and raise the existing alerting mechanism. Inspect `FraudAlert` and `PaymentHold` and choose the one meant for "review before pay"; prefer a hold for the first run. Report your choice. Arabic reason «الراتب خارج حدود الدرجة».
- **No salary at all** (null and no grade): keep today's behaviour behind `Payroll:RequireBaseSalaryOnActivation` (default `false`, so the FUN-001 journey keep working), but log a warning and raise the same alert. When the option is `true`, create the profile with `IsActive = false`.
- Keep idempotency and the existing deactivate/replace logic for existing profiles.

---

## Workstream I — Hire checklist: appointment decision and medical step (G-22, D-6)

**I1. Steps.** Append to `HireStep` (stored as string): `AppointmentDecisionIssued`, `PreEmploymentMedical`, with Arabic display names «إصدار قرار التعيين» and «الفحص الطبي قبل التعيين».

**I2. Configuration**

- `HireChecklistDefaults.DefaultSteps` becomes configurable through `HireChecklistOptions` (`HireChecklist:RequireAppointmentDecision` = true, `HireChecklist:RequireMedicalFitness` = false).
- `SeedChecklistAsync` seeds only the enabled steps.
- `GovernanceFor`:
  - decision → `hr-contracts`, 48 h, evidence required
  - medical → `hr-health`, 120 h, evidence required
- Existing open transactions are **not** back-filled. Document this.

**I3. Appointment decision**

- On `HireWizard/Step2`, a button «إصدار قرار التعيين» (`HrContractsHireExecute`) creates an `AdministrativeDecision`:
  - `Subject` «قرار تعيين: {name} — {position}»
  - `DecisionNumber` from `ICodeGenerationClient` — add a code type following COD-001 if none fits, and document it
  - `IssuedBy`, `IssuedDate` = today, `RelatedEmployeeId`
- It links `ServiceTransaction.AppointmentDecisionId` and marks `AppointmentDecisionIssued` complete with `EvidenceRef` = the decision number.
- Idempotent: a second click shows the existing decision.
- Leave `// PLAN-29 follow-up: register in Diwan / sign`.

**I4. Medical step** — manual completion on `Step2` with an evidence reference (report or document number) and the completing user. Leave `// PLAN-29 follow-up: consume HR.Health fitness result`.

**I5. Completion**

- `Step4` completion already requires all items. Verify that the new items are respected, and that `CanPublishHireCompleted` requires them before the saga.
- Update `HireWizard` views to show the new rows with owner and SLA.

---

## Workstream J — Commencement watch (detection only)

- **Worker** `HireCommencementWatcher.FlagOverdueAsync(DateOnly todayUtc)` in HR.Contracts, run by a `BackgroundService` job following HR.Contracts' or HR.Core's leader-election job pattern (grep `AdvisoryLeaderElection`; daily; enabled flag; disabled in test hosts). For each open `Appointment` transaction:
  - with `ExpectedStartDate` set
  - with no `CommencementRecord` for the employee/contract
  - where `ExpectedStartDate + Recruitment:CommencementDeadlineDays` (default 30; put the option in HR.Contracts config) `< today`
  - and `CommencementOverdueAt == null`

  it sets `CommencementOverdueAt` and publishes `IProcessInterventionRequested` (Reason «تأخر المباشرة عن المهلة»).
- **UI:** an «متأخر عن المباشرة» badge in `HireWizard/Index` and `Step2`.
- Leave `// PLAN-29 follow-up: cancel hire for non-commencement → vacate position, reopen requisition, recall reserve (see PLAN-27 marker)`.

---

## Workstream K — Tests

### Unit

**`Fun029CandidateAcceptedV4HandlerTests`**
- v4 terms stored on the contract
- lineage columns set
- v3 message still works
- internal-mobility message ignored
- redelivery idempotent

**`Fun029ContractTypeResolverTests`**
- specificity order
- inactive maps ignored
- fallback on/off
- unresolved → no contract + checklist note + intervention published (test harness)

**`Fun029LineageBackfillTests`**
- a Postgres integration test proving the migration backfill fills the column from notes
- use `Skip.If` without Docker

**`Fun029ContractActivatedTests`**
- salary/grade published from the contract

**`Pay029PayProfileTests`**
- grade resolved in the dated scale
- salary from the event
- out of range → profile + alert/hold
- no salary → current behaviour + warning
- option true → inactive profile

**`Fun029ChecklistTests`**
- seeded steps follow options
- decision creation idempotent and marks the step
- completion blocked until decision (and medical when enabled)

**`Fun029CommencementWatchTests`**
- flags overdue once
- ignores commenced
- ignores future

### Acceptance — whole PLAN-29 (`src/Tests/HRManagement.AcceptanceTests/AcHcm29HireContractTests.cs`)

- Cover **AC-HCM-29a..i** from the plan, adapted to these prompts, with `EvidenceRecorder("AC-HCM-29", correlationId)`:
  - a: hire → contract with type/grade/salary
  - b: duplicate person
  - c: mismatch
  - d: activation → pay profile salary/grade
  - e: unresolved type → intervention
  - f: offer PDF + hash
  - g: decision required
  - h: lineage columns
  - i: internal mobility → no appointment
- Personas: existing HR core/contracts/approver/payroll personas — check `personas.json` for `a-contracts-maker`, `a-contracts-checker`, `payroll-processor`.
- Use `Skip.If` — **never** `WriteResult("Skip"); return;`.
- If the local stack is available, run it and produce `acceptance-evidence/AC-HCM-29/`; otherwise report that it was not run.

### Regression

Keep green, or matching the baseline:
- `Fun001`, `HireCase`, `HireWizard`, `HireSagaRecruitmentLineage`, `Ac15CorrelationIdHireJourney`, the hire E2E journey
- `Hcm026`–`Hcm029`
- the Payroll inbound-event tests

List every modified test with a justification.

---

## Workstream L — Script and docs (cover both sessions)

- `scripts/hcm029-validate.ps1`, modelled on `scripts/hcm028-validate.ps1`: checks the HR.Core and HR.Contracts migrations and files, then runs `Hcm029|Fun029|Pay029` and the hire regression.
- `docs/acceptance-plans/execution/PLAN-29-EXECUTION.md`: prerequisites (contract-type mappings for acceptance), commands, personas, Arabic manual steps (hire → contract terms → activation → pay profile; decision; medical toggle; intervention screen; overdue commencement), evidence folder.
- `PLAN-29-hcm-hire-contract-documents.md`:
  - Status → `Implemented (acceptance evidence pending)` or `Implemented`
  - "Implementation notes / deviations" for both sessions; list the deferred follow-ups explicitly
- `docs/HCM_001_RECRUITMENT.md`: hire closure safety, v4 event, offer letter, mark G-12/G-13/G-22 as addressed.
- `docs/HIRE_ONBOARDING_GUIDE.md`: the new checklist steps, contract-type mapping, intervention, offer letter.
- `docs/MESSAGE_EVENT_MATRIX.md`: `ICandidateAccepted` v4, internal mobility, `IContractActivated` values.
- `docs/FUN_001_JOURNEY_MATRIX.md`, `docs/HIRE_RBAC_MATRIX.md`: new steps and owners; mapping admin policy.
- `docs/DATABASE_SCHEMA_INVENTORY.*` (generator if one exists).

---

## Commit plan

6. `feat(fun029): contract lineage columns and offer terms from ICandidateAccepted v4`
7. `feat(fun029): contract-type resolution by service regime with intervention; mapping admin`
8. `feat(fun029): IContractActivated carries salary and grade; payroll pay-profile fix`
9. `feat(fun029): appointment decision and optional pre-employment medical in hire checklist`
10. `feat(fun029): commencement watch (overdue detection + intervention)`
11. `test(hcm029): integration + acceptance AC-HCM-29a..i`
12. `docs(hcm029): plan status, execution, guides, event matrix, validate script`

Every commit builds; tests green at 6–10 and 12.

## Definition of done (whole PLAN-29)

- An external hire produces:
  - an `Appointment` transaction with explicit lineage
  - a pending contract of the right type, carrying the offer's grade/salary/regime/employment type
  - after activation, a pay profile with that grade and salary (out-of-range salaries are flagged)
- Unresolved contract types never disappear silently.
- Internal mobility no longer opens an appointment.
- The hire case requires an appointment decision (and medical fitness when enabled), and overdue commencements are flagged.
- The offer letter PDF is available with a verification hash.
- Build has 0 errors; `has-pending-model-changes` reports none for HR.Core, HR.Contracts and Payroll; tests pass; `scripts/hcm029-validate.ps1` passes.
- Acceptance tests are written, and run with evidence or explicitly reported as not run.

## Final report (English; quote Arabic UI strings as-is; cover both sessions)

1. Summary per workstream (A–D from the handoff, verified; E–L).
2. Commits of both sessions.
3. Migrations per service and data steps.
4. Configuration keys added and their defaults per environment.
5. Test results vs. baseline; modified tests with justification.
6. Acceptance: run or not (why), evidence path.
7. Deviations and judgement calls (H alert mechanism, F4 policy, null handling in F1).
8. Deferred follow-ups and `// PLAN-2x:` markers (file:line).
9. Manual test script (Arabic UI paths).
10. `git log --oneline` of the branch.
