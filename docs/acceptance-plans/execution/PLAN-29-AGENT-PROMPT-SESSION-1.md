# Task (Session 1 of 2): PLAN-29 Part 1 — Safe hire closure in HR.Core, offer terms in the hire event, official offer letter

## Role

You are a senior .NET 10 / ASP.NET Core MVC / EF Core (PostgreSQL) / MassTransit engineer in the `HRManagement` microservices repository on a Windows development machine (PowerShell, Docker Desktop). Work autonomously and follow the rules below strictly.

**This is session 1 of 2 for PLAN-29.**

- **This session implements the HR.Core side:**
  - Step 0 (baseline)
  - **A** — person matching and hire errors
  - **B** — employment type aligned with the domain
  - **C** — `ICandidateAccepted` v4 carrying the offer terms, and no hire event for internal mobility
  - **D** — official offer letter PDF
  - unit tests
- **Session 2 will implement:**
  - the HR.Contracts side (lineage columns, contract terms, contract-type resolution, appointment decision, medical step, commencement watch)
  - the Payroll pay-profile fix
  - acceptance tests and docs

  **Do not implement those here.**
- At the end, write a handoff file for session 2.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-29-hcm-hire-contract-documents.md` (Arabic) — including the «تعديلات قبل التنفيذ» note at the top. This prompt refines the plan. **Where they differ, this prompt wins.**
2. `docs/HCM_001_GAP_STUDY.md` — gaps **G-12** and **G-13**.
3. The handoffs and "Implementation notes" of PLAN-26/27/28 (`reports/plan2[678]-session1-handoff.md` and the plan docs). They describe conventions you must reuse: `RecruitmentOptions`, the offer workflow handler, `OfferEligibilityRules`, the minutes PDF pattern from PLAN-28, `ApplicationRejection`, and the hire block on pending appeals.
4. Read these fully before changing them:
   - `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/{RecruitmentHireClosureService,RecruitmentOfferService,OfferLetterWorkflowHandler,RecruitmentOptions}.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Integration/RecruitmentIntegrationService.cs`
   - `src/Services/HRCore/HR.Core.Infrastructure/Data/{HRCoreDbContext,EmployeePiiProtectionInterceptor}.cs` (Employee `NationalId` is encrypted at rest; `NationalIdHash` is maintained by the interceptor; unique `(TenantId, NationalIdHash)`)
   - `src/Services/HRCore/HR.Core.Web/Controllers/{RecruitmentApplicationsController,OfferLettersController}.cs` (the `Hire` GET/POST; today the POST catches only `InvalidOperationException`, ~line 497)
   - `ViewModels/RecruitmentHireViewModel.cs` (today's categories: PERMANENT/TEMPORARY/CONTRACT/INTERNSHIP)
   - `Views/RecruitmentApplications/Hire.cshtml`, `Views/OfferLetters/Details.cshtml`
   - `src/Shared/HRManagement.SharedKernel/Enums/EmploymentType.cs` — `Permanent`, `Administrative`, `Temporary`; matches «دائم – إداري – متعاقد مؤقت» in `docs/Requariments.md`
   - `src/Shared/HRManagement.MessageContracts/Events/{ICandidateAccepted,IInternalPlacementCompleted,IEmployeeCreated}.cs`
   - `src/Shared/HRManagement.SharedKernel/Legal/{ServiceRegimeToPayrollMapper,LegalSourceToRegimeMapper}.cs` (or wherever they live — grep)
   - the PLAN-28 minutes document class and `src/Shared/HRManagement.Documents.QuestPdf/*` (PDF pattern)
   - the message-contract/compatibility tests (grep the test projects for `ICandidateAccepted` and `SchemaVersion`, and check `docs/rel-compat/`)

## Context and scope boundaries

- **PLAN-25 is deferred.** No changes to generic workflow gates.
- **Out of scope here:**
  - all HR.Contracts and Payroll code (session 2)
  - candidate download of the offer letter, and notifications (PLAN-30 — markers only)
  - archiving PDFs in File.Storage (marker only)
- **`ICandidateAccepted` v4 is a planned, additive contract change.** New members must have default interface implementations, so existing consumers compile and older messages deserialize. Do not remove or rename members.
- Keep diffs minimal. Follow the existing style; Arabic user-facing messages; nonce CSP (no inline handlers or styles).
- **Never commit:**
  - `global.json` (local change)
  - `scripts/__pycache__/*`
  - `docs/thesis_diagrams/source/.$*`
  - `tmp-vendors.html`
  - secrets
  - test-rewritten `testdata/*/out/*.json` — the working tree currently has many of these modified; restore them first with `git checkout -- testdata`

## Verified repository facts

- **Hire closure today:**
  - `RecruitmentHireClosureService.HireAndCreateEmployeeAsync` creates an `Employee` without checking for an existing person. A duplicate national ID hits the unique `NationalIdHash` index (`DbUpdateException`), and the controller turns that into a 500.
  - The national ID typed in the hire form is never compared with the application's.
  - `EmploymentRecord.EmploymentType` and `IEmployeeCreated.EmploymentType` are hard-coded to `Permanent` (~lines 318 and 393).
  - `ICandidateAccepted` is published **unconditionally** (~line 365), including for internal mobility (`isInternalMobility`, ~line 157); `IInternalPlacementCompleted` is also published for internal mobility (~line 405). The only `ICandidateAccepted` consumer is `HR.Contracts` `CandidateAcceptedConsumer`, which opens an `Appointment` transaction — wrong for an existing employee.
- **Payroll today:** `HandleContractActivatedHandler` sets `BaseSalary = request.BaseSalary ?? grade?.BaseSalary ?? 0`. HR.Contracts publishes `BaseSalary/PayGradeCode` as null today, so hires get a 0-salary pay profile. Session 2 fixes this using the v4 fields you add here.
- **PDF pattern:** documents are generated on demand (Payroll payslips, PLAN-28 committee minutes). HR.Core has no server-side upload to File.Storage.
- **Migrations:**
  - Command: `dotnet ef migrations add HCM029_<Purpose> -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web` (after the last `HCM028_*`).
  - Guard Postgres-only SQL with `migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"`.
  - `has-pending-model-changes` must report none.
- **Tests:** unit tests use SQLite in-memory `HRCoreDbContext`; integration tests use Testcontainers PostgreSQL (with Npgsql exceptions); acceptance tests use the local stack.

## Stop and ask if

- a migration would rewrite or delete business data;
- an existing contract/compatibility test forbids the additive `ICandidateAccepted` change in a way you cannot satisfy by updating the snapshot/baseline;
- some other consumer of `ICandidateAccepted` turns out to rely on receiving it for internal mobility;
- the code differs materially from what is described here.

---

## Step 0 — Baseline

1. `git checkout -- testdata`, then from `main` (which now contains PLAN-26/27/28) create branch `feature/plan-29-hire-contract-documents`. It must be clean apart from local noise.
2. Run `dotnet build HRManagement.slnx`, then:

   ```powershell
   dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm02|Recruitment|Hire|Fun001|Offer|Syr001|Syr003|MessageContract"
   ```

   Record the results in `reports/plan29-baseline.md`. Restore `testdata` again before committing.

---

## Workstream A — Person matching and safe hire errors (G-12)

**A1. `HirePersonMatchService`** (HR.Core.Infrastructure/Services/Recruitment):

- `Task<Employee?> FindByNationalIdAsync(string nationalId, CancellationToken)` normalizes the ID (trim; reuse any existing normalizer) and queries **by `NationalIdHash`** (`PiiMask.Sha256Hex`) — never by the encrypted column.
- Optional second signal: same normalized full name **and** birth date (reuse `FullNameNormalized` if it exists). Return it as a "possible match" only; never block on it.

**A2. Hire GET**

- If the application has a national ID and a matching employee exists: open the form in **«ربط موظف موجود»** mode with that employee pre-selected, and show a banner «يوجد موظف مسجَّل بنفس الرقم الوطني: {name} — سيتم الربط به.»
- A possible (name + birth date) match shows a softer warning.

**A3. Hire POST**

| Case | Behaviour |
|------|-----------|
| CreateNew mode, an employee already has that national ID | model error «الرقم الوطني مسجَّل لموظف قائم — استخدم «ربط موظف موجود».» No exception, no 500 |
| Application has a national ID that differs from the entered one (CreateNew) or from the selected employee's (LinkExisting, compared by hash) | require `NationalIdMismatchReason` (≥ 10 chars); store it in an `EntityAuditLog` entry on the application (`Action = "HireNationalIdMismatch"`); never log the IDs themselves, only the fact and the reason |

**A4. Error handling**

- New `HireClosureException(HireClosureErrorKind kind, string messageAr)` with kinds `DuplicatePerson`, `Concurrency`, `PositionNotVacant`, `InvalidState`, `Legal`.
- Inside `ExecuteHireClosureAsync`:
  - map `DbUpdateException` whose inner exception is a `PostgresException` with `SqlState == "23505"` → `DuplicatePerson`
  - map `DbUpdateConcurrencyException` → `Concurrency`
  - existing `InvalidOperationException` throws may stay or become `HireClosureException` (your choice; be consistent)
- The controller catches `HireClosureException` **and** `InvalidOperationException` and re-renders the form with the Arabic message.
- Make sure the transaction is rolled back and no event is published when the closure fails. Check how the outbox and transaction interact in this method today, and keep it that way.

---

## Workstream B — Employment type aligned with the domain

- `RecruitmentHireViewModel.EmploymentCategories` is built from `EmploymentType` (`Permanent` «دائم», `Administrative` «إداري», `Temporary` «متعاقد مؤقت»), with values = enum names.
- **Legacy values still arriving** (`PERMANENT`/`TEMPORARY`/`CONTRACT`/`INTERNSHIP`): map PERMANENT→Permanent, TEMPORARY→Temporary, CONTRACT→Temporary. **Reject INTERNSHIP** with a message (there is no domain value; do not invent one).
- **Default:** use the funded position's or job catalog's employment type if such a field exists (grep). Otherwise use `RecruitmentOptions.DefaultEmploymentType` (`Permanent`). Choosing a different value than the default requires `EmploymentTypeChangeReason` (≥ 10 chars, audited).
- `EmploymentRecord.EmploymentType` and `IEmployeeCreated.EmploymentType` use the chosen value (replace the hard-coded `Permanent`).

---

## Workstream C — `ICandidateAccepted` v4 and internal mobility (G-13, HR.Core side)

**C1. Contract** — add members with defaults (`=> null` / `=> false`) and `SchemaVersion => 4`:

| Member | Type |
|--------|------|
| `OfferedSalary` | decimal? |
| `GradeCode` | string? |
| `LegalRegimeId` | string? |
| `PayrollRegimeCode` | string? |
| `EmploymentType` | string? (enum name from B) |
| `PositionCode` | string? |
| `OrgUnitId` | Guid? |
| `IsInternalMobility` | bool |

- Keep `EmploymentCategory` for compatibility, filled with the same enum name.
- Update any contract snapshot/compatibility test or baseline file accordingly. List what you changed.

**C2. Publishing** (`PublishHireEventsAsync`)

- **External hire:** publish v4 with all fields:
  - offer: `OfferedSalary`, `GradeCode`
  - legal regime: the `EmploymentRecord.LegalRegimeId` already computed; `ServiceRegimeToPayrollMapper.MapToPayrollCode`
  - employment type: from B
  - position: `PositionCode`, `OrgUnitId`
- **Internal mobility:** do **not** publish `ICandidateAccepted` any more; keep `IInternalPlacementCompleted` as is. Leave `// PLAN-29 session 2: HR.Contracts also ignores IsInternalMobility (defence in depth)`.
- Verify the FUN-001 saga and `Ac15CorrelationIdHireJourneyTests`/E2E hire paths are external hires and remain unaffected. Report the result.

---

## Workstream D — Official offer letter (part of G-18)

**D1. Document.** `OfferLetterDocument`, following the PLAN-28 minutes class and `HrOfficialDocumentLayout`, Arabic RTL. Contents:

- agency/org unit header
- offer number, issue date, expiry date
- candidate name (no national ID)
- position title, position code, grade, offered salary, proposed start date, employment type
- legal reference label (SYR-001)
- conditions text from options (`RecruitmentOptions.OfferLetterConditionsAr`, a short default)
- a **verification code** (see D2)

Add a QR code only if a QR library is already referenced in `Directory.Packages.props`. Do not add a new package for it.

**D2. Integrity at issue**

- When the offer becomes `Issued` (`OfferLetterWorkflowHandler` approved branch, and any other path that issues), compute **SHA-256 over a canonical JSON** of the rendered fields (stable property order, invariant culture, ISO dates). Store it on the offer:
  - `DocumentSha256` (char 64)
  - `DocumentGeneratedAt`
  - migration `HCM029_OfferDocument`
- The PDF prints the first 12 hex characters as «رمز التحقق».
- Rendering later must reproduce the same hash. If any rendered field changes after issue (it should not), show a warning on Details instead of silently re-hashing.

**D3. Download**

- `GET OfferLetters/Document/{id}` (`HrCoreReadOnly`) streams the PDF, only when the status is `Issued` or `Accepted`.
- Add a link on `OfferLetters/Details` and on the application Details offer history.
- Leave `// PLAN-30: candidate download from /careers` and `// PLAN-29 follow-up: archive in File.Storage`.

---

## Tests (unit; integration where noted)

**`Hcm029PersonMatchTests`**
- match by hash
- no match
- the possible-match signal does not block
- GET pre-selects LinkExisting

**`Hcm029HireErrorsTests`**
- CreateNew with an existing national ID → validation error (no exception reaches the controller)
- national-ID mismatch without a reason rejected; with a reason → audited and no IDs in the audit payload
- concurrency/unique violations map to `HireClosureException`

Add an **integration** test on PostgreSQL proving that a real unique violation becomes `DuplicatePerson` and that no outbox message is written.

**`Hcm029EmploymentTypeTests`**
- categories from the enum
- legacy mapping
- INTERNSHIP rejected
- non-default without a reason rejected
- `EmploymentRecord`/`IEmployeeCreated` carry the chosen type

**`Hcm029CandidateAcceptedV4Tests`**
- published fields for an external hire (use the MassTransit test harness or the existing publish-capture pattern)
- internal mobility publishes only `IInternalPlacementCompleted`
- a v3-shaped message still deserializes into the v4 interface with defaults

**`Hcm029OfferDocumentTests`**
- PDF bytes non-empty
- hash stored at issue
- hash is stable across renders
- download refused for Draft/PendingApproval

**Regression** — green, or matching the baseline: `Hcm026`, `Hcm027`, `Hcm028`, `RecruitmentHireClosure`, `RecruitmentHire`, `Fun001`, `HireSagaRecruitmentLineage`, `Syr001`, `Syr003`. List every modified existing test with a one-line justification.

---

## Commit plan

1. `feat(hcm029): person matching and safe hire errors`
2. `feat(hcm029): employment type aligned with domain enum`
3. `feat(hcm029): ICandidateAccepted v4 with offer terms; no hire event for internal mobility`
4. `feat(hcm029): official offer letter PDF with verification hash`
5. `docs(hcm029): session 1 handoff`

Every commit builds; tests green at 3, 4 and 5.

## Definition of done (session 1)

- The hire form never produces a 500 for expected data.
- A duplicate person is prevented and steered to linking, and national-ID mismatches are justified and audited.
- The employment type is a domain value.
- External hires publish `ICandidateAccepted` v4 with the offer terms; internal mobility no longer opens an appointment.
- Issued offers have a reproducible verification hash and a downloadable PDF.
- Build has 0 errors; `has-pending-model-changes` reports none; tests pass.

## Handoff (mandatory last step)

Write and commit `reports/plan29-session1-handoff.md` with:

1. Branch and commits.
2. The migration and its columns.
3. The **exact v4 contract** (member list, defaults, SchemaVersion), and an example payload for an external hire.
4. Employment type values and the legacy mapping.
5. New and changed types and paths.
6. `RecruitmentOptions` additions.
7. Test results vs. baseline; modified tests.
8. Deviations, known issues, `// PLAN-2x:` markers (file:line).
9. What session 2 must implement on the consumer side.

Then reply with a short summary.
