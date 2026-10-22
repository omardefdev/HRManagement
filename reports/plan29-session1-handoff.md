# PLAN-29 — Session 1 handoff (Step 0, A, B, C, D — HR.Core side)

**Date:** 2026-10-22
**Branch:** `feature/plan-29-hire-contract-documents` (local only — not pushed), created from `main` @ `e9a240a` (contains PLAN-26/27/28).

## 1. Commits

| SHA | Message |
|-----|---------|
| `df6c674` | feat(hcm029): person matching and safe hire errors (includes `reports/plan29-baseline.md`) |
| `74cbff8` | feat(hcm029): employment type aligned with domain enum |
| `0680e7d` | feat(hcm029): ICandidateAccepted v4 with offer terms; no hire event for internal mobility |
| `0f21f43` | feat(hcm029): official offer letter PDF with verification hash |
| *(this commit)* | docs(hcm029): session 1 handoff |

Not committed (local noise): `global.json`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, `tmp-vendors.html`, `testdata/*/out/*.json` (restored with `git checkout -- testdata` before each commit).

## 2. Migration

`20261022162341_HCM029_OfferDocument` (after `20261022112349_HCM028_AppealGovernance`). New nullable columns only — no data step, nothing rewritten, no Postgres-only SQL.

| Table | Column | Type |
|-------|--------|------|
| `OfferLetters` | `DocumentSha256` | `character(64)` null (`HasMaxLength(64).IsFixedLength()`) |
| `OfferLetters` | `DocumentGeneratedAt` | `timestamp with time zone` null |

`dotnet ef migrations has-pending-model-changes` → **No changes**. Offers issued before the migration keep `null` (Details says no hash was recorded; the PDF prints «غير مسجَّل»). The acceptance seeder's directly-`Issued` offer (`AcceptanceDataSeeder.cs:~769`) is in that state too.

## 3. `ICandidateAccepted` v4 (exact contract)

`src/Shared/HRManagement.MessageContracts/Events/ICandidateAccepted.cs` — additive; nothing removed or renamed.

| Member | Type | Default | Since |
|--------|------|---------|-------|
| `EmployeeId` | `Guid` | — | v1 |
| `EmploymentCategory` | `string` | — | v1 — **now the `EmploymentType` enum name** (was `PERMANENT`/…) |
| `ExpectedStartDate` | `DateTime` | — | v1 |
| `FullName` | `string?` | — | v1 |
| `SourceSystem` | `string` | — | v1 |
| `CorrelationId` | `string?` | — | v1 |
| `TenantId` | `Guid?` | `=> null` | v2 |
| `RecruitmentApplicationId`, `RequisitionId`, `FundedPositionId`, `OfferLetterId` | `Guid?` | `=> null` | v3 |
| `OfferedSalary` | `decimal?` | `=> null` | **v4** — `OfferLetter.OfferedSalary` |
| `GradeCode` | `string?` | `=> null` | **v4** — `OfferLetter.GradeCode ?? FundedPosition.GradeCode` |
| `LegalRegimeId` | `string?` | `=> null` | **v4** — `EmploymentRecord.LegalRegimeId` (law50/law53/special_*) |
| `PayrollRegimeCode` | `string?` | `=> null` | **v4** — `ServiceRegimeToPayrollMapper.MapToPayrollCode(LegalRegimeId)` |
| `EmploymentType` | `string?` | `=> null` | **v4** — enum name, same value as `EmploymentCategory` |
| `PositionCode` | `string?` | `=> null` | **v4** — `FundedPosition.PositionCode` |
| `OrgUnitId` | `Guid?` | `=> null` | **v4** — `FundedPosition.OrgUnitId ?? Employee.PrimaryOrgUnitId` |
| `IsInternalMobility` | `bool` | `=> false` | **v4** — always `false` on published messages |
| `SchemaVersion` | `int` | `=> 4` | publisher also sets `SchemaVersion = 4` explicitly |

A v3 payload read through MassTransit gets `null`/`false` for the v4 members and keeps its own `SchemaVersion` (3) — proven by `Hcm029CandidateAcceptedV4Tests.V3_shaped_message_is_read_through_the_v4_interface_with_defaults`.

Example payload (external hire, from the unit-test scenario):

```json
{
  "employeeId": "6f1c…", "employmentCategory": "Permanent", "expectedStartDate": "2026-11-21T00:00:00",
  "fullName": "الفائز", "sourceSystem": "HR.Core", "correlationId": "…", "tenantId": "…",
  "recruitmentApplicationId": "…", "requisitionId": "…", "fundedPositionId": "…", "offerLetterId": "…",
  "offeredSalary": 5000, "gradeCode": "G5", "legalRegimeId": "law50", "payrollRegimeCode": "LAW_50_2004",
  "employmentType": "Permanent", "positionCode": "VAC-H27-A", "orgUnitId": "…",
  "isInternalMobility": false, "schemaVersion": 4
}
```

Publishing (`RecruitmentHireClosureService.PublishHireEventsAsync`):
- **External hire** (`SourceInternalApplicationId == null`, CreateNew or LinkExisting): `ICandidateAccepted` v4, then `IEmployeeCreated`.
- **Internal mobility**: `IInternalPlacementCompleted` only (consumed by HR.Movements `InternalPlacementCompletedConsumer`, which drafts a transfer). No `ICandidateAccepted` → HR.Contracts no longer opens an `Appointment` for an existing employee.
- Side fix: the regime id is now read from the change tracker first, so `IEmployeeCreated.LegalRegimeId`/`PayrollRegimeCode` are filled for new hires (the old `AsNoTracking` query ran before the record was saved and returned null).

Compatibility files changed: `docs/rel-compat/event-schema-inventory.json` (`ICandidateAccepted` schemaVersion 1→4 — the entry was stale at 1 while the code was at 3; publisher, compatRule, notes), `docs/MESSAGE_EVENT_MATRIX.md` (v4 row + `IInternalPlacementCompleted` row), `scripts/hcm001-validate.ps1` (accepts `SchemaVersion => 3` or `4`). No snapshot/baseline test forbade the change.

FUN-001 check: `Fun001HireSaga*`, `HireSagaRecruitmentLineageTests` and `Ac15CorrelationIdHireJourneyTests` have no internal-mobility path (Ac15 publishes `IEmployeeCreated` directly; the lineage test feeds HR.Contracts notes directly). Integration `Fun001*`/`HireSagaRecruitmentLineage` pass; Ac15 is an acceptance test and was not run (needs `RUN_ACCEPTANCE`).

## 4. Employment type

Values (`HRManagement.SharedKernel.Enums.EmploymentType`, posted as enum names): `Permanent` «دائم», `Administrative` «إداري», `Temporary` «متعاقد مؤقت».

Legacy codes (`HireEmploymentTypeRules.LegacyCodes`, case-insensitive): `PERMANENT`→`Permanent`, `TEMPORARY`→`Temporary`, `CONTRACT`→`Temporary`. `INTERNSHIP` is refused with «فئة «تدريب عملي» غير معرّفة…» (no domain value invented). Numbers and unknown codes are refused.

Default: funded positions and the job catalog have **no** employment-type field (grepped), so the default is `Recruitment:DefaultEmploymentType` (`Permanent`). A different choice needs `EmploymentTypeChangeReason` (≥ 10 chars) → `EntityAuditLog` `HireEmploymentTypeChange` `{defaultType, chosenType, reason}`. Enforced in the form and again in the closure service.

`EmploymentRecord.EmploymentType`, `IEmployeeCreated.EmploymentType`, `ICandidateAccepted.EmploymentCategory/EmploymentType` all use the chosen value.

## 5. New and changed types

### New

| Type | Path |
|------|------|
| `HireNationalIdRules` (hash compare, reason ≥ 10, `MismatchAuditAction = "HireNationalIdMismatch"`) | `HR.Core.Domain/Services/HireNationalIdRules.cs` |
| `HireEmploymentTypeRules` (categories, legacy map, `TryResolve`, `ChangeAuditAction = "HireEmploymentTypeChange"`) | `HR.Core.Domain/Services/HireEmploymentTypeRules.cs` |
| `HireClosureException : InvalidOperationException` + `HireClosureErrorKind {DuplicatePerson, Concurrency, PositionNotVacant, InvalidState, Legal}` + `FromSaveFailure(DbUpdateException)` | `HR.Core.Infrastructure/Services/Recruitment/HireClosureException.cs` |
| `HirePersonMatchService(HRCoreDbContext)` + `HirePersonMatch(NationalIdMatch, PossibleMatch)` — `FindByNationalIdAsync`, `FindByNationalIdHashAsync` (tagged `HCM029:HirePersonMatch`), `FindPossibleMatchAsync(fullName, birthDate)`, `MatchAsync(hash, fullName, birthDate)` | `…/Recruitment/HirePersonMatchService.cs` (DI scoped) |
| `HireClosureJustification(NationalIdMismatchReason, ActorUserId, EmploymentTypeChangeReason)` | `…/Recruitment/RecruitmentHireClosureService.cs` |
| `OfferLetterDocumentService(db, ILegislationRegisterProvider, IOptions<RecruitmentOptions>)` — `StampAsync`, `GetAsync`, `CanonicalJson`, `ComputeSha256`, `CanDownload`, `VerificationCodeOf`; records `OfferLetterDocumentData`, `OfferLetterDocumentView` | `…/Recruitment/OfferLetterDocumentService.cs` (DI scoped) |
| `OfferLetterDocument.Generate(OfferLetterDocumentView)` (QuestPDF, `ConfigureOfficialPage`) | `HR.Core.Web/Services/OfferLetterDocument.cs` |

### Changed

| Item | Change |
|------|--------|
| `RecruitmentHireClosureService` (ctor unchanged) | `HireAndCreateEmployeeAsync`/`HireExistingEmployeeAsync` gained `HireClosureJustification? justification = null` before the token; duplicate pre-check by hash; national-id mismatch check + audit; employment-type resolve/reason/audit; `PositionNotVacant`/regime errors → `HireClosureException`; `DbUpdateException` 23505 on the `NationalIdHash` index → `DuplicatePerson`, other 23505 / `DbUpdateConcurrencyException` → `Concurrency`; **change tracker cleared on any failure** (the transaction already rolled back on dispose; clearing drops the employee and the outbox rows the bus outbox added, so a later `SaveChanges` in the scope writes nothing); v4 publishing (§3). Pre-PLAN-29 validation throws stay `InvalidOperationException` (tests assert the exact type). Private `SeedEmployeeSideEffectsAsync` gained `EmploymentType employmentType` |
| `RecruitmentIntegrationService` | passes the justification through |
| `OfferLetterWorkflowHandler(db, OfferLetterDocumentService? documents = null)` | approved → `MarkIssued` + `StampAsync`; built by hand without the service it falls back to default options |
| `OfferLetter` | + `DocumentSha256`, `DocumentGeneratedAt` |
| `RecruitmentApplicationsController` | ctor + `HirePersonMatchService personMatch` (last); Hire GET pre-selects LinkExisting + banner; POST: duplicate / mismatch / employment-type validation, catches `HireClosureException` and `InvalidOperationException`, re-renders **keeping the user's input** (`RedisplayHireAsync`); the matched employee is always in the employee list |
| `OfferLettersController` | ctor + `OfferLetterDocumentService documents` (last); `GET Document/{id}` (class-level `HrCoreReadOnly`, `SecurityAudit` BulkExport); Details fills `vm.Document` |
| `RecruitmentHireViewModel` | categories from the enum; + `NationalIdMismatchReason`, `ApplicationHasNationalId`, `MatchedEmployeeId/Name`, `PossibleMatchEmployeeName`, `DefaultEmploymentCategory`, `EmploymentTypeChangeReason` |
| `OfferLetterDetailsViewModel` | + `Document` |
| Views | `RecruitmentApplications/Hire.cshtml` (tab follows `Mode`, banners, mismatch reason, type reason toggle — nonce script, no inline handlers/styles); `OfferLetters/Details.cshtml` (PDF button, verification code, mismatch/no-hash alerts); `RecruitmentApplications/Details.cshtml` (PDF link in offer history) |

## 6. `RecruitmentOptions` additions

```csharp
public const string DefaultEmploymentTypeKey = "Recruitment:DefaultEmploymentType";
public EmploymentType DefaultEmploymentType { get; set; } = EmploymentType.Permanent;   // read via IConfiguration at hire
public const string DefaultOfferLetterConditionsAr = "يُعدّ هذا العرض نافذاً حتى تاريخ انتهاء صلاحيته، …";
public string OfferLetterConditionsAr { get; set; } = DefaultOfferLetterConditionsAr;  // Recruitment:OfferLetterConditionsAr
```

No appsettings changes.

## 7. Tests

### New (all pass)

| Class | Count | Covers |
|-------|-------|--------|
| `Hcm029PersonMatchTests` (unit) | 6 | hash match (encrypted column ignored), no match, possible match does not block, possible match dropped when it is the id match, GET pre-selects LinkExisting, GET stays CreateNew |
| `Hcm029HireErrorsTests` (unit) | 10 | CreateNew duplicate → form error (no exception, nothing saved); closure duplicate → `HireClosureException` + empty tracker + no publish; mismatch w/o reason (form + closure); LinkExisting mismatch; justified mismatch audited without ids or hashes; matching id → no audit; 23505 mapping (id index / other index), concurrency, unrelated failures unmapped; position not vacant |
| `Hcm029EmploymentTypeTests` (unit) | 19 (incl. theories) | categories = enum; legacy mapping; INTERNSHIP/unknown refused (rules, form, closure); non-default w/o reason refused; chosen type on record + both events + audit; legacy = default needs no reason; configured default pre-selected |
| `Hcm029CandidateAcceptedV4Tests` (unit) | 4 | v3 implementation gets defaults; external hire v4 fields via a MassTransit round trip; internal mobility publishes only `IInternalPlacementCompleted`; v3 payload read through v4 |
| `Hcm029OfferDocumentTests` (unit) | 12 (incl. theories) | hash stored at issue; handler without the service still stamps; rejection → no hash; stable across renders and cultures; changed field detected, not re-hashed; PDF bytes; download refused for Draft/PendingApproval/Declined; streams for Issued/Accepted; unknown → 404 |
| `Hcm029HireDuplicatePersonIntegrationTests` (Testcontainers PostgreSQL + real MassTransit EF outbox) | 1 | a concurrent insert after the pre-check → real 23505 → `DuplicatePerson`, application untouched, `OutboxMessage` count unchanged, later `SaveChanges` writes 0; control hire writes outbox rows |

### Results vs. baseline

| Run | Baseline | Now |
|-----|----------|-----|
| Baseline filter (`Hcm02|Recruitment|Hire|Fun001|Offer|Syr001|Syr003|MessageContract`) | 423 pass / 2 fail | **474 pass / 2 fail** (same 2) |
| Full unit suite | — | 3075 pass / 8 fail — exactly the pre-existing set from the PLAN-26/27/28 handoffs: the 2 above + `BolaArchitectureTests…` (HRContracts/Payroll), `Ten002…IgnoreQueryFilters` (`BenefitPlanSeeder.cs`), `Prv001RopaTests…`, `San001SanctionsTests.Validate_script_passes` (local `global.json`), `Str005ProductionBindingTests` ×2 (expired declared exception) |
| Integration (`Hcm02|RecruitmentHire|InternalChannel|InternalHireClosure|Fun001|HireSagaRecruitmentLineage|Syr00|Recruitment|SoDRequisition|Offer`) | — | 40 pass / 1 fail — see below |

`EstablishmentHireIntegrationTests` (2 tests, picked up by the `RecruitmentHire`-ish filter) fail: its service collection registers `EstablishmentService` but not `IFundedPositionVersionService`, which `ProcessPartyAckConsumers` requires, so the consumer cannot be built. Neither file is touched by this branch (`ProcessPartyAckConsumers.cs` last changed in `544ba7a`). Not fixed here.

`dotnet build HRManagement.slnx` → 0 errors.

### Modified existing tests

| File | Change | Why |
|------|--------|-----|
| `IntegrationTests/RecruitmentHireClosureIntegrationTests.cs` | seeded application no longer carries `NationalId = "0101010101"` | each test types a different national id; the new mismatch rule would refuse them. The comparison is covered by `Hcm029HireErrorsTests` |
| `IntegrationTests/InternalChannelIntegrationTests.cs` (`InternalHireClosure…`) | expects 1 publish (not 2) and asserts no `ICandidateAccepted` | internal mobility no longer publishes `ICandidateAccepted` (C2) |
| `IntegrationTests/RecruitmentTestControllers.cs` | passes `new HirePersonMatchService(db)` | controller ctor gained the service |
| `UnitTests/Syr006ServiceRegimeBindingTests.cs` | reflection call passes `EmploymentType.Permanent` | private `SeedEmployeeSideEffectsAsync` gained the parameter |

## 8. Deviations, known issues, markers

**Deviations from the prompt**
- *Possible match on GET:* `RecruitmentApplication` has no birth date, so the name + birth-date warning cannot appear on the first GET; it appears when the form is re-shown after a POST (using the typed birth date). The national-id banner works on GET.
- *23505 mapping:* only a violation on the `NationalIdHash` index (or with no constraint name) is `DuplicatePerson`; other unique indexes (e.g. funded-position versions on a concurrent hire of the same position) map to `Concurrency`, so the user is not told "duplicate person" for a position race.
- *Exception types:* `HireClosureException` derives from `InvalidOperationException`. New failure modes and position/regime errors use it; the pre-existing state/offer/legal-source/appeal throws stay plain `InvalidOperationException` because existing tests assert the exact type.
- *Organization name* in the PDF header is a layout constant and not hashed (HR.Core.Infrastructure does not reference the QuestPDF project); the org unit is hashed.
- *No QR code:* no QR library in `Directory.Packages.props`.

**Known issues**
- The hashed fields include config/reference-derived values: `OfferLetterConditionsAr`, the default employment type label, and the legislation `DisplayLabel`. Changing any of them after issue makes issued offers show the "data changed" warning (by design — the content did change — but there is no per-offer snapshot of those values). A follow-up could snapshot them at issue.
- The offer letter shows the **default** employment type as «نوع التوظيف المقترح»; the actual type is chosen at hire.
- Offers issued before the migration (and the acceptance seeder's issued offer) have no hash.

**Markers**
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs:550` — `// PLAN-29 session 2: HR.Contracts also ignores IsInternalMobility (defence in depth)`
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/OfferLetterDocumentService.cs:66` — `PLAN-29 follow-up: archive in File.Storage`
- `src/Services/HRCore/HR.Core.Web/Controllers/OfferLettersController.cs:406` — `PLAN-30: candidate download from /careers`
- `src/Services/HRCore/HR.Core.Web/Controllers/OfferLettersController.cs:407` — `PLAN-29 follow-up: archive in File.Storage`
- Existing, untouched: `SelectionCommitteesController.cs` `// PLAN-29: archive minutes via File.Storage`

## 9. What session 2 must implement (consumer side)

1. **`CandidateAcceptedConsumer` / `ProcessCandidateAcceptedCommand`** (`HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs:23`, `HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs`): pass the v4 members into the command; **return early when `IsInternalMobility` is true** (defence in depth — HR.Core no longer sends them). v3 messages (`OfferedSalary == null`) must keep today's behaviour.
2. **Lineage columns** (`FUN029_ContractOfferTerms`): `ServiceTransaction.RecruitmentApplicationId/RequisitionId/OfferLetterId` from the message (all already on v3), backfill from `Notes` via `ParseRecruitmentApplicationId`. Note `EmploymentCategory` in notes is now the enum name (`Permanent`) instead of `PERMANENT` — plain text, nothing parses it.
3. **Contract terms**: `Contract.GradeCode` ← `GradeCode`, `AgreedBaseSalary` ← `OfferedSalary`, `LegalRegimeId` ← `LegalRegimeId`, `SourceOfferLetterId` ← `OfferLetterId`.
4. **`ContractTypeResolver`**: key `(LegalRegimeId, EmploymentType)`; `EmploymentType` values are exactly `Permanent`/`Administrative`/`Temporary`; `LegalRegimeId` values as in `docs/syr/service-regimes.json` (law50/law53/special_*), may be null (unmapped legal source → HR.Core opened a `ServiceRegimeException`). `PayrollRegimeCode` is already mapped (`LAW_50_2004`/`LAW_53_2021`/null).
5. **`IContractActivated`** (`MassTransitEventPublisher.cs:43-45`): `BaseSalary = contract.AgreedBaseSalary`, `PayGradeCode = contract.GradeCode`, then the Payroll `HandleContractActivatedHandler` fix.
6. Appointment decision step, medical step, commencement watch, acceptance tests (AC-HCM-29a..i) and docs (`HIRE_ONBOARDING_GUIDE.md`, `FUN_001_JOURNEY_MATRIX.md`, plan status) — untouched here.
7. Optional: fix `EstablishmentHireIntegrationTests` DI (register `IFundedPositionVersionService`) if it gets in the way of the FUN-001 regression run.
