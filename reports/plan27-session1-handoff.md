# PLAN-27 — Session 1 handoff (Step 0, A, B, C, D)

**Date:** 2026-10-21
**Branch:** `feature/plan-27-offer-merit-reserve` (local only — not pushed), created from the tip of `feature/plan-26-assessment-interview-requisition` @ `94c7ce7` (PLAN-26 is not merged to `main`).

## 1. Commits

| SHA | Message |
|-----|---------|
| `9a65c17` | feat(hcm027): offer eligibility rules + merit order |
| `b1e6a95` | feat(hcm027): exception approval workflow (override, distinct approvers, persona) |
| `89e1a0b` | feat(hcm027): offer outcomes — decline/revoke/expire, new application statuses |
| `dac5d1a` | feat(hcm027): offer expiry job |
| *(this commit)* | docs(hcm027): session 1 handoff |

The baseline (`reports/plan27-baseline.md`) is part of commit `9a65c17`.

## 2. Migration

`20261021195823_HCM027_OfferOutcome` (generated after `20261021114833_HCM026_RequisitionIntegrity`), table `OfferLetters`, **new columns only**, no data step:

| Column | Type |
|--------|------|
| `IsMeritOverride` | boolean, not null, default false |
| `MeritOverrideReason` | varchar(1000) null |
| `ValidityDays` | integer, not null, **default 14** (hand-edited from EF's 0 so existing rows get the option default) |
| `DeclineReason` | varchar(1000) null |
| `DeclinedAt` | timestamptz null |
| `DeclinedBy` | varchar(20) null — `Candidate` \| `HrOnBehalf` |
| `RevokeReason` | varchar(1000) null |
| `RevokedAt` | timestamptz null |
| `ExpiredAt` | timestamptz null |

`OfferStatus` and `RecruitmentApplicationStatus` are stored as **int** (`OfferLetters` has the filtered index `"Status" IN (2, 3)`), so the appended enum members need no migration.
`dotnet ef migrations has-pending-model-changes` → **No changes**.

## 3. New and changed types

### Domain (`src/Services/HRCore/HR.Core.Domain`)
| Type | Path | Notes |
|------|------|-------|
| `OfferEligibilityRules` (new) | `Services/OfferEligibilityRules.cs` | `Validate(...)`, `OfferableStatuses` {Ranked, ReserveListed}, `IsAvailableForMerit(app)`, `OccupiesRequisition(offer)`, messages, `MinMeritOverrideReasonLength = 30` |
| `OfferOverrideApprovalRules` (new) | `Services/OfferOverrideApprovalRules.cs` | `WorkflowCode`, `SameApproverMessage`, `IsOverrideWorkflow(code)`, `HasApprovedEarlierStep(stepHistoryJson, currentLevel, actorId)` |
| `OfferWorkflowRules` (changed) | `Services/OfferWorkflowRules.cs` | + `MinValidityDays`/`MaxValidityDays` (3/60), `MinDeclineReasonLength` (5), `MinRevokeReasonLength` (10), `ValidityDaysRangeMessage`, `IsValidityDaysInRange`, `ComputeExpiryDate(issuedUtc, days)` (end of UTC day, 23:59:59), `MarkIssued(offer, nowUtc)`, `CanRevoke`, `CanExpire(offer, nowUtc)` |
| `SelectionWorkflowRules` (changed) | `Services/SelectionWorkflowRules.cs` | new transitions (§5), `SystemOnlyTargets` += OfferDeclined, ReserveListed; `OfferOutcomeIsAutomaticMessage`; `SystemOnlyTargetMessage(to)`; **public `TerminalStatuses`** |
| `RecruitmentApplicationStatus` (changed) | `Enums/RecruitmentApplicationStatus.cs` | `OfferDeclined = 11` «عرض مرفوض/منتهٍ», `ReserveListed = 12` «قائمة احتياط» |
| `OfferLetter` (changed) | `Entities/OfferLetter.cs` | columns of §2; `OfferDeclinedBy` constants class (`Candidate`, `HrOnBehalf`, `IsValid`) in the same file |

### Infrastructure (`src/Services/HRCore/HR.Core.Infrastructure`)
| Type | Path | Notes |
|------|------|-------|
| `RecruitmentOfferService` (changed) | `Services/Recruitment/RecruitmentOfferService.cs` | ctor + `IOptions<RecruitmentOptions>`; see §5 |
| `RecruitmentOfferExpirer` (new) | `Services/Recruitment/RecruitmentOfferExpirer.cs` | scoped; `ExpireDueAsync(DateTime nowUtc, CancellationToken)` |
| `RecruitmentOfferExpiryJob` (new) | `Services/Recruitment/RecruitmentOfferExpiryJob.cs` | advisory lock **8675313** |
| `OfferLetterWorkflowHandler` (changed) | `Services/Recruitment/OfferLetterWorkflowHandler.cs` | approved → `OfferWorkflowRules.MarkIssued` |
| `RecruitmentRequisitionCascade` (changed) | `Services/Recruitment/RecruitmentRequisitionCascade.cs` | `TerminalApplicationStatuses` = `[.. SelectionWorkflowRules.TerminalStatuses]` (now `internal`); cascade-revoked offers get `RevokeReason`/`RevokedAt` |
| `RecruitmentRequisitionLifecycleService` (changed) | line ~288 | PLAN-27 marker replaced by an explanatory comment (no behaviour change) |
| `RecruitmentHireClosureService` (changed) | sibling rejection skips `OfferDeclined` |
| `RecruitmentSelectionService` (changed) | system-only target message per status |
| `WorkflowInstanceActionService` (changed) | `ValidateSelfApprovalAsync(instance, definition, actorUserId, ct)` — gained the `definition` parameter; distinct-approver rule for the override workflow; creator SoD kept |
| `WorkflowPackSeeder`, `PolicyEngineSeeder`, `PolicyEngineDomainSeeds`, `PolicyStaticFallback` (changed) | see §6 |
| `AcceptanceDataSeeder` (changed) | seeded Issued offer `ExpiryDate = ReferenceDate + 2 years` |

### Shared
| Item | Path |
|------|------|
| `PolicyDomainFactsBuilders.ForOfferLetter(..., string? contextJson = null, bool isMeritOverride = false)` | `src/Shared/HRManagement.ServiceDefaults/Policy/PolicyDomainFactsBuilders.cs` |
| `AcceptancePersonaCatalog` — 56 personas, Agency A 38 | `src/Shared/HRManagement.ServiceDefaults/Seeding/AcceptancePersonaCatalog.cs` |

No change in `src/Shared/HRManagement.MessageContracts`.

### Web (`src/Services/HRCore/HR.Core.Web`)
| Item | Notes |
|------|-------|
| `RecruitmentApplicationsController` | `Details` loads `OfferLetters` (split query), `BestAvailableRank`, `RequiresMeritOverride`, `DefaultOfferValidityDays`; `CreateOffer` passes `ValidityDays`, `IsMeritOverride`, `MeritOverrideReason` (ctor unchanged) |
| `OfferLettersController` | `Index(..., bool meritOverride)`; `DeclineOffer(id, DeclineOfferForm)` (always `HrOnBehalf`); `Revoke(id, RevokeOfferForm)` (ctor unchanged) |
| View models (`RecruitmentSelectionViewModels.cs`) | `CreateOfferForm.ExpiryDate` → `ValidityDays` (3..60), + `IsMeritOverride`, `MeritOverrideReason`; new `DeclineOfferForm`, `RevokeOfferForm`; `OfferLettersIndexViewModel.MeritOverrideOnly`; details VM + `BestAvailableRank`, `RequiresMeritOverride`, `DefaultOfferValidityDays` |
| `Views/RecruitmentApplications/Details.cshtml` | offer form for Ranked/ReserveListed only; validity days input; override checkbox + reason rendered server-side only when not next by merit; «التالي بالجدارة» badge on the rank board; «سجل العروض» table |
| `Views/OfferLetters/Details.cshtml` | exception badge + reason; validity and outcome fields; decline/revoke forms with reasons (no inline handlers; the pre-existing accept form keeps its old `onsubmit`) |
| `Views/OfferLetters/Index.cshtml` | «عروض استثنائية فقط» filter + badge |
| `Services/OfferLetterWorkflowUiService.cs` | panel block reason for a same approver on the override workflow |
| `Program.cs` | `AddScoped<RecruitmentOfferExpirer>()`, hosted `RecruitmentOfferExpiryJob` when `Recruitment:Jobs:OfferExpiryEnabled` |

## 4. `RecruitmentOptions` additions

```csharp
public int OfferValidityDays { get; set; } = 14;              // Recruitment:OfferValidityDays (service accepts 3..60)
// RecruitmentJobsOptions
public bool OfferExpiryEnabled { get; set; } = true;          // Recruitment:Jobs:OfferExpiryEnabled
public int OfferExpiryIntervalMinutes { get; set; } = 60;     // Recruitment:Jobs:OfferExpiryIntervalMinutes
```

No `appsettings*.json` keys were added (defaults apply). Test hosts set `Recruitment:Jobs:OfferExpiryEnabled=false` next to the intake-close setting.

## 5. Public signatures for session 2

```csharp
// RecruitmentOfferService — ctor changed (new last parameter)
RecruitmentOfferService(HRCoreDbContext db, IPolicyOrchestrator policyOrchestrator, ICodeGenerator codeGenerator,
    RecruitmentLegalSourceValidator legalSourceValidator, IWorkflowStarter workflowStarter,
    IConfiguration configuration, IOptions<RecruitmentOptions> recruitmentOptions)

const string OfferApprovalWorkflowCode = "RECRUITMENT-OFFER-APPROVAL";
const string OfferOverrideApprovalWorkflowCode = "RECRUITMENT-OFFER-OVERRIDE-APPROVAL";
const string ExpiryNote = "انتهاء صلاحية العرض";
int DefaultValidityDays { get; }

Task<(OfferLetter? Offer, string? Error)> CreateDraftAsync(
    Guid applicationId, decimal offeredSalary, string? gradeCode, DateTime proposedStartDate,
    int? validityDays,                        // was DateTime? expiryDate; null → Recruitment:OfferValidityDays
    Guid? actorUserId = null,
    bool isMeritOverride = false, string? meritOverrideReason = null,
    CancellationToken ct = default)
    // order: requisition link → OfferEligibilityRules.Validate → validity range → existing active offer → legal source
    // Ranked/ReserveListed → OfferExtended

Task<int?> GetBestAvailableRankAsync(Guid requisitionId, CancellationToken ct = default)
    // min Rank of Ranked|ReserveListed, Rank != null, AppealStatus != Pending

Task<(bool Success, string? Error)> SubmitForApprovalAsync(Guid offerId, Guid? actorUserId, string? actorUserName, CancellationToken ct = default)
Task<(bool Success, string? Error)> AcceptAsync(Guid offerId, CancellationToken ct = default)   // now requires app OfferExtended
Task<(bool Success, string? Error)> DeclineAsync(Guid offerId, string? reason, string declinedBy, Guid? actorUserId = null, CancellationToken ct = default)
Task<(bool Success, string? Error)> RevokeAsync(Guid offerId, string? reason, bool revertToRanked, Guid? actorUserId = null, CancellationToken ct = default)
Task<(bool Success, string? Error)> ExpireAsync(Guid offerId, DateTime nowUtc, CancellationToken ct = default)
internal void ApplyExpiry(OfferLetter offer, DateTime nowUtc)   // stages only; offer must include RecruitmentApplication

// OfferEligibilityRules (domain)
string? Validate(RecruitmentApplication app, int? bestAvailableRank, bool requisitionHasOtherActiveOffer,
    bool isMeritOverride, string? meritOverrideReason)
IReadOnlySet<RecruitmentApplicationStatus> OfferableStatuses   // Ranked, ReserveListed
bool IsAvailableForMerit(RecruitmentApplication)
bool OccupiesRequisition(OfferLetter)                            // Draft|PendingApproval|Issued|Accepted&&!converted

// RecruitmentApplication transitions (SelectionWorkflowRules)
Ranked        → OfferExtended | ReserveListed | Rejected | Withdrawn
ReserveListed → OfferExtended | Rejected | Withdrawn            // OfferExtended needs ActiveOfferLetterId (gate)
OfferExtended → OfferAccepted | OfferDeclined | Rejected | Withdrawn
OfferDeclined → (terminal)
SystemOnlyTargets = { Assessed, OfferDeclined, ReserveListed }  // CanTransition still returns true for them;
                                                                  // only GetAllowedTargets/TransitionStatusAsync exclude them
TerminalStatuses  = { Hired, Rejected, Withdrawn, OfferDeclined } // public; use this, not the cascade's internal array

// RecruitmentRequisitionCascade (internal static)
internal static readonly RecruitmentApplicationStatus[] TerminalApplicationStatuses = [.. SelectionWorkflowRules.TerminalStatuses];
// RevocableOfferStatuses (private) = Draft, PendingApproval, Issued — unchanged
```

`RecruitmentApplicationStatus.ReserveListed` is never assigned by session 1 code — session 2 owns that (use `SelectionWorkflowRules.CanTransition(app, ReserveListed)`; the manual path rejects it).

## 6. Workflow and persona

- **Workflow** `RECRUITMENT-OFFER-OVERRIDE-APPROVAL`, id `f0050015-0015-4000-8000-000000000015`, entity `OfferLetter`, steps: 1 «مراجعة HR» (`hr-core`), 2 «اعتماد إداري» (`hr-management-approver`), 3 «اعتماد استثناء الجدارة» (`hr-management-approver`). `// PLAN-25: move step 3 to compliance` above the definition. Policy workflow binding added in `PolicyEngineSeeder`.
- **Distinct approvers:** any actor who approved an earlier step of the same override instance is refused («لا يجوز لنفس المعتمد اعتماد أكثر من خطوة في عرض استثنائي.»). Creator SoD unchanged. The step guard/engine were not touched.
- **Policy:** rule `R-OFF-OVERRIDE` (priority 9, `action == Issue && isMeritOverride == true` → TriggerWorkflow override). `PolicyEvaluator` orders matched rules by ascending priority and `GetWorkflowCode()` takes the first TriggerWorkflow, so it wins over `R-OFF-ISSUE` (10) for override offers and does not match normal ones — no engine change. Facts schema gains `isMeritOverride` (bool) and `recruitmentApplicationId` (string). Static fallback mirrors it.
- **Persona:** `a-management-approver-2` / `acc-a-management-approver-2`, role `hr-management-approver`, Agency A, HQ employee ordinal 25, env var **`ACCEPTANCE_A_MANAGEMENT_APPROVER_2_PASSWORD`** (optional override; otherwise `prepare-keycloak-import.ps1` generates a password into the git-ignored `.acceptance/credentials.json`). No password committed. Per-persona env vars are documented only in `personas.json` (no doc lists them); session 2 should mention it in the PLAN-27 execution guide. The catalog size is asserted in four places, all updated to 56 / Agency A 38: `AcceptancePersonaCatalog`, `prepare-keycloak-import.ps1`, `AcceptanceV2CatalogTests` (also 50 employee links, 54 enabled), `AcceptanceIdentityPopulationTests`. Keycloak must be re-imported for the persona to exist.

## 7. Test results

| Run | Baseline | Now |
|-----|----------|-----|
| Prompt filter (`Hcm02\|Recruitment\|Offer\|SelectionWorkflow\|RequisitionWorkflow\|Fun001\|Syr001`) | 197 passed / 2 failed | covered by the wide run below |
| Wide regression (prompt filter + `Hcm00\|Policy\|NationalSeed\|Workflow\|Acceptance\|Establishment\|Est0`) | — | **679 passed / 2 failed** (same 2) |
| `Hcm027*` | — | **69 passed** |
| Full unit suite | PLAN-26: 2767 passed / 8 failed | **2860 passed / 8 failed** — the same 8 pre-existing failures listed in the PLAN-26 handoff |
| Acceptance catalog tests (`AcceptanceV2CatalogTests`, `AcceptanceIdentityPopulationTests`) | — | 7 passed / 1 skipped |

Pre-existing failures (unchanged): `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider`, `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50` (expects `Issued` right after submit in Off mode; gets `PendingApproval`), plus in the full suite `Ten002…IgnoreQueryFilters_only_in_sanctioned_bypass` (`BenefitPlanSeeder.cs`), `BolaArchitectureTests…` (HRContracts/Payroll controllers), `Prv001RopaTests…`, `San001SanctionsTests.Validate_script_passes`, `Str005ProductionBindingTests` ×2.

Build: `dotnet build HRManagement.slnx` → 0 errors (all projects, including acceptance and integration tests). Integration and acceptance (live) tests were compiled, **not run**.

New test classes: `Hcm027TestSupport` (fixtures), `Hcm027OfferEligibilityRulesTests`, `Hcm027OfferServiceEligibilityTests`, `Hcm027OverrideWorkflowTests` (lab path, 3-step definition, same approver on 2+3, creator, 3 distinct approvers → Issued, static fallback, seeded rules in Enforce mode — asserts the ledger matched `R-OFF-OVERRIDE`/`R-OFF-ISSUE`, not the fallback), `Hcm027OfferOutcomeTests`, `Hcm027OfferExpirerTests`, `Hcm027SelectionTransitionsTests` (transitions, allowed targets, cancel/close cascade on ReserveListed/OfferDeclined).

Modified existing tests:
- `Syr001OfferLegalSourceTests` — the Ranked seed gets `Rank = 1` (Ranked always has a rank; merit order now requires it); new `RecruitmentOptions` ctor argument.
- `Hcm022RecruitmentWorkflowsUiTests` — new `RecruitmentOptions` ctor argument.
- `IntegrationTests/RecruitmentTestControllers` — new `RecruitmentOptions` ctor argument.
- `IntegrationTests/{CrossTenantApiPenetration,Est004EstablishmentPlanning,HRCoreJwtBearer,TenantQuarantine}IntegrationTests` — `Recruitment:Jobs:OfferExpiryEnabled=false`.
- `AcceptanceTests/AcceptanceV2CatalogTests`, `AcceptanceIdentityPopulationTests` — persona counts (renamed `Persona_catalog_has_56_…`).

## 8. Deviations, known issues, markers

Deviations:
- The two new enum members landed in commit 1 (A needs `ReserveListed` in `OfferEligibilityRules`); their transitions came in commit 3 as planned.
- Migration `HCM027_OfferOutcome` holds the B **and** C columns (the prompt makes it shared) and therefore landed in commit 2; the C columns are unused until commit 3.
- `ValidateSelfApprovalAsync` gained a `WorkflowDefinition definition` parameter (single call site) instead of re-querying the definition.
- The distinct-approver check lives in a domain helper (`OfferOverrideApprovalRules`) so the action service and the workflow panel share it.
- `recruitmentApplicationId` was added to the CoreRecruitment facts schema: `ForOfferLetter` already sent it but the schema did not declare it, so Enforce-mode offer submission was rejected by fact validation («حقل غير مسموح»). Without this the policy-mode part of B4 could not work.
- `AcceptAsync` now refuses unless the application is `OfferExtended` (closes the Assessed → OfferAccepted jump).
- `RevokeAsync(revertToRanked: true)` is refused when the application has no rank.
- Decline/revoke/expiry move the application only when the offer is its active offer (or it has none); otherwise only a note is added.
- Accept/submit forms keep their existing inline `onsubmit` confirms (untouched); the replaced decline/revoke forms have none.
- Some touched files were stored with CRLF in git; `.gitattributes` (`eol=lf`) normalised them on commit, so e.g. `RecruitmentOfferService.cs` shows a whole-file diff in `9a65c17` (`git diff -w` shows the real change).

Known issues / limits:
- Existing acceptance databases are not re-seeded: their seeded Issued offer (expiry `2026-08-22`) will be expired by the job on first run (the application becomes OfferDeclined). That offer was already not acceptable (`CanAccept` checks the expiry). New seeds use ReferenceDate + 2 years.
- `ExpiryDate` of offers issued before this change is unchanged; their `ValidityDays` is 14 (column default) but has no effect.
- On hire, `RecruitmentHireClosureService` still rejects `ReserveListed` siblings (only OfferDeclined was excluded). Session 2 must decide whether the reserve list survives a hire.
- The job can only run on PostgreSQL (`AdvisoryLeaderElection` no-ops elsewhere); the expirer itself was tested on SQLite.
- UI verified by compilation and service-level tests only — not exercised in a browser.
- Local-only: `global.json` (`rollForward`) left uncommitted.

Markers:
- `src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs:65` — `// PLAN-25: move step 3 to compliance`
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferService.cs:233` (decline), `:284` (revoke), `:313` (expiry) — `// PLAN-30: notify candidate`
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferExpirer.cs:20` — `// PLAN-30: reminder 48h before expiry`
- `src/Services/HRCore/HR.Core.Web/Controllers/OfferLettersController.cs:360` — PLAN-30 candidate self-service decline
- `src/Services/HRCore/HR.Core.Domain/Entities/OfferLetter.cs:97` — `OfferDeclinedBy.Candidate` reserved for PLAN-30
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionLifecycleService.cs:288` — the former `// PLAN-27:` marker is now an HCM-027 explanation (ReserveListed rejected on close).

## 9. Notes for session 2

- Continue on `feature/plan-27-offer-merit-reserve`. New migrations go after `20261021195823_HCM027_OfferOutcome`.
- **Grade/salary + Payroll lookup:** validate inside `CreateDraftAsync` after `OfferEligibilityRules.Validate` (the form still posts `OfferedSalary`/`GradeCode`). Any new ctor dependency of `RecruitmentOfferService` must also be added to `Hcm027TestSupport.CreateOfferService`, `Syr001OfferLegalSourceTests`, `Hcm022RecruitmentWorkflowsUiTests` and `IntegrationTests/RecruitmentTestControllers`.
- **Reserve list:** assign `ReserveListed` from `Ranked` (allowed transition, system-only). `GetBestAvailableRankAsync` and `OfferEligibilityRules` already treat ReserveListed as offerable, so after a decline/expiry the next candidate by merit (Ranked or ReserveListed) is eligible without override. Reserve expiry should probably move ReserveListed → Rejected (the only allowed exit besides OfferExtended/Withdrawn).
- Cancel/close already reject ReserveListed (open). If the reserve list must survive a close, change `RecruitmentRequisitionLifecycleService.CloseAsync` (~line 288) — today it does not.
- The offer form defaults the validity from `RecruitmentOfferService.DefaultValidityDays`; acceptance tests should not rely on a free expiry date any more.
- Acceptance: use `a-management-approver` + `a-management-approver-2` for steps 2/3 of the override workflow and a central `hr-core` (not the creator) for step 1. Remember TempData messages are not observable through the gateway (PLAN-26) — assert DB state.
- Docs to write in session 2: PLAN-27 status/execution guide (mention `ACCEPTANCE_A_MANAGEMENT_APPROVER_2_PASSWORD`, the Keycloak re-import and the three new options), `HCM_001_RECRUITMENT.md` (G-06/G-07), `HIRE_ONBOARDING_GUIDE.md` (merit order, exception offers, decline/revoke/expiry).
