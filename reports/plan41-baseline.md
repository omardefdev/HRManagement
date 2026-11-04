# PLAN-41 baseline (P0)

Taken on 2026-11-04 on `main` at `e5071b42` (branch head `520ec8a4`, docs only), before any change to the product, the stack or the
tests. Images built from that tree; evidence went to `artifacts/plan41/`, so the committed `acceptance-evidence/` was not touched,
apart from two classes that wrote into it directly (the writes were reverted and those classes fixed, see below).

## GitHub state (read-only)

| Item | Value |
|---|---|
| Visibility | PUBLIC |
| Branch protection on `main` | 404 "Branch not protected" (not 403: the API is available) |
| Repository variables | `ENABLE_SCHEDULED=false`; `ENABLE_ACCEPTANCE_JOB` does not exist, so there is nothing to delete after the merge |
| Last 5 `ci.yml` runs on `main` | all success: 41801829202, 41410147253, 41360824124, 41296399636 (deduped, about 1 minute each), 41247555134 (full, 74 minutes) |
| Billable minutes | public repository: 0 |
| Last PR run (41795960463) | CI 24.5 min, CodeQL 16.8 min, E2E 18.7 min after CI; 43.5 min wall-clock |

## Contract with PLAN-37 (step 0.1)

Met: `docker-compose.acceptance-prodlike.yml` exists and `acceptance-up.ps1 -ProdLike` starts it (240 s from empty volumes).

## Class inventory (step 0.3)

74 classes, 203 test methods; PLAN-33 had 63 classes and 103 methods. The eleven new classes are AcceptanceGateTests,
RecruitmentStatesFileTests, AcHcm25, AcInt37ProductionWiring, AcInt37VpsServer, AcMsg40, AcOps36, AcOps38, AcOps39, AcQa34, AcQa35.
None disappeared; AcHcm21, AcHcm22 and AcWf23 grew. `*VpsChecks.cs` are partials of `AcOps36VpsBootTests`, not classes.
The inventory, one row per class, is the source of `tests/acceptance/ci-scope.json`; it came from a read-only pass (912 s).

**Stops or recreates containers:** Ac17, Ac18, Ac22 (excluded, chaos); AcHcm26, AcHcm27 (`docker restart hr-core-web`);
AcInt37 (`docker stop`/`start hr-core-web`); AcMsg40 (`docker stop rabbitmq`, restarts HR.Core); AcOps38 (its own project); AcOps39
(`up --force-recreate` of every service); AcOps36 (excluded).

**Found in the inventory, fixed in this plan:** `powershell` hard-coded in AcHcm25, AcMsg40, AcOps38; AcInt37 asserting the JSON
spacing of PowerShell 5.1; AcHcm25 and AcMsg40 writing into `acceptance-evidence/` past `ACCEPTANCE_EVIDENCE_DIR`; AC-QA-35c asserting
that the Acceptance job is skipped and that all jobs succeed although the dedupe guard skips them since PLAN-37; the CI artifact
`test-results` holding no trx at all (`**/TestResults/*.trx` does not match `TestResults/unit/*.trx`).

## Current results (step 0.4)

Lab: `up` 254 s. Reset 6 s, prod-like `up` 240 s. Run one group at a time under `ACCEPTANCE_REQUIRE_ALL=true`, on Windows PowerShell
5.1. Not run here: the excluded classes, AcOps38 (third stack), AcQa34, AcQa35, and the prod-like journey, so AC-37b reports that.

| Group | Result | Seconds |
|---|---|---|
| lab-main (56 classes) | 88 passed, 20 failed, 3 skipped | 814 |
| AcHcm26 | 5 passed | 136 |
| AcHcm27 | 4 passed | 276 |
| AcHcm25 (prod-like) | 8 passed | 43 |
| AcInt37 (prod-like) | 6 passed, 2 failed | 143 |
| AcMsg40 (prod-like) | 8 passed | 95 |
| AcOps39 (prod-like) | 8 passed | 79 |

**Required classes that were red, with the cause** (read-only root cause, `artifacts/plan41/est-triage.md`; none is a new product
defect):

| Class | Cause | Kind |
|---|---|---|
| AcEst10 | the plan target is computed before three patches add 55 BR1 positions | seed order |
| AcEst14 | sends the enum as a string; MVC has no string converter | test |
| AcEst15, AcEst17 | expect a change to apply on submit; since PLAN-25 it needs two approvals | test |
| AcEst18 a/b | reads `value`, the lookup returns `code` | test |
| AcEst18 d | the seeded job catalog uses G1..G10, MDM holds only GR-* | seed |
| AcEst24 a/e | no pending change in the seed; 24e also uses a persona that can never see the item | test |
| AcHcm28 b, AcHcm31 a | "connection forcibly closed" on a reused connection, once each: to watch | environment |
| AcInt37 b/e | need the prod-like journey first | order |
| AcceptanceV2Catalog, IdentityPopulation | constants of 60 personas, the catalog has 61 | test |

AC-EST-14 goes green with a code that starts with a dash: the national template is hidden from agency tenants. That is the known
defect PLAN-48 D-TEN-9, so `ac-ownership.json` gives it `provesClaim: false` and owner PLAN-48.

**Not required, red on PowerShell 5.1 or in their own right** (AC-01..37 show and never fail the job): Ac08, Ac19, Ac21, Ac23, Ac24,
Ac25, Ac26, Ac28, Ac30. Ac21 and Ac28 are script limits of 5.1 (quoting to `docker exec`, `HttpClient` not loaded). Ac12 stays Blocked
(PLAN-59).

## Summary before (step 0.5)

With the old generator on the committed evidence: **38 Pass, 2 Deferred/Blocked, 0 Fail, 20 not run** (43 rows; the plan's 27/2/3/23
was from 2026-10-30). The same folders under the new rules, before any new run: **0 proven, 0 passed but not proving, 41 stale, 6
incomplete, 30 not run, 0 fail, 0 blocked**: no folder records a code tree, and AC-34/36 count as not run.

## Per class

| Class | Part | Group | Required | Passed | Failed | Other | Seconds |
|-------|------|-------|----------|--------|--------|-------|---------|
| Ac01CentralTenantIsolationTests | lab | main | no | 1 | 0 | 0 | 1 |
| Ac02BranchManagerScopeTests | lab | main | no | 1 | 0 | 0 | 0 |
| Ac03SiblingAccessDeniedTests | lab | main | no | 1 | 0 | 0 | 0 |
| Ac04CrossTenantGuessTests | lab | main | no | 1 | 0 | 0 | 0 |
| Ac05PlatformEnterTenantTests | lab | main | no | 2 | 0 | 0 | 1 |
| Ac06TransferAccessTests | lab | main | no | 1 | 0 | 0 | 15 |
| Ac07AgencyOnboardingTests | lab | main | no | 1 | 0 | 0 | 1 |
| Ac08QuarantineReconciliationTests | lab | main | no | 0 | 1 | 0 | 0 |
| Ac09ShadowPolicyTests | lab | main | no | 1 | 0 | 0 | 0 |
| Ac10EnforcePolicyTests | lab | main | no | 1 | 0 | 0 | 1 |
| Ac11PolicyWorkflowTests | lab | main | no | 1 | 0 | 0 | 1 |
| Ac12PayrollSodTests | lab | main | no | 0 | 0 | 1 | 0 |
| Ac13ParallelCodeGenerationTests | lab | main | no | 1 | 0 | 0 | 1 |
| Ac15CorrelationIdHireJourneyTests | lab | main | no | 1 | 0 | 0 | 6 |
| Ac16DuplicateMessageTests | lab | main | no | 1 | 0 | 0 | 8 |
| Ac19AnalyticsReconciliationTests | lab | main | no | 0 | 1 | 0 | 1 |
| Ac20DatabaseIsolationTests | lab | main | no | 1 | 0 | 0 | 42 |
| Ac21PayrollDeterminismTests | lab | main | no | 0 | 1 | 0 | 1 |
| Ac23PayrollFreezeTests | lab | main | no | 0 | 1 | 0 | 1 |
| Ac24PartialDisbursementTests | lab | main | no | 0 | 1 | 0 | 1 |
| Ac25FraudHoldTests | lab | main | no | 0 | 1 | 0 | 2 |
| Ac26NationalIdPrivacyTests | lab | main | no | 0 | 1 | 0 | 1 |
| Ac27DsarExportTests | lab | main | no | 1 | 0 | 0 | 2 |
| Ac28FileScanTests | lab | main | no | 0 | 1 | 0 | 1 |
| Ac29FileAccessExpiryTests | lab | main | no | 1 | 0 | 0 | 12 |
| Ac30S2sFileReferenceRejectionTests | lab | main | no | 0 | 1 | 0 | 1 |
| Ac31MassAssignmentTests | lab | main | no | 1 | 0 | 0 | 1 |
| Ac32LogisticsScopeLiveTests | lab | main | no | 1 | 0 | 0 | 2 |
| Ac33AssetLossLiveTests | lab | main | no | 1 | 0 | 0 | 0 |
| Ac37JourneyRebuildLiveTests | lab | main | no | 1 | 0 | 0 | 0 |
| AcceptanceCiScopeTests | lab | main | yes | - | - | - | - |
| AcceptanceCoverageTests | lab | main | yes | 2 | 0 | 0 | 0 |
| AcceptanceEvidenceSummaryTests | lab | main | yes | 2 | 0 | 0 | 0 |
| AcceptanceGateTests | lab | main | yes | 12 | 0 | 0 | 0 |
| AcceptanceIdentityPopulationTests | lab | main | yes | 0 | 1 | 0 | 0 |
| AcceptanceTenantConsistencyTests | lab | main | yes | 2 | 0 | 0 | 0 |
| AcceptanceV2CatalogTests | lab | main | yes | 6 | 1 | 0 | 0 |
| AcEst10EstablishmentGapTests | lab | main | yes | 0 | 1 | 0 | 0 |
| AcEst11BranchGapScopeTests | lab | main | yes | 1 | 0 | 0 | 0 |
| AcEst12FinanceReconciliationTests | lab | main | yes | 1 | 0 | 0 | 0 |
| AcEst13EstablishmentMdmLookupTests | lab | main | yes | 1 | 0 | 0 | 0 |
| AcEst14JobCatalogCodPreviewTests | lab | main | yes | 0 | 1 | 0 | 0 |
| AcEst15JobCatalogBulkImportTests | lab | main | yes | 0 | 1 | 0 | 0 |
| AcEst16JobCatalogImportValidationTests | lab | main | yes | 1 | 0 | 0 | 1 |
| AcEst17JobCatalogMigrationLegacyTests | lab | main | yes | 1 | 1 | 0 | 1 |
| AcEst18ReferenceCatalogAdminTests | lab | main | yes | 1 | 3 | 0 | 1 |
| AcEst24EstablishmentWorkflowUiTests | lab | main | yes | 1 | 0 | 2 | 0 |
| AcHcm06CandidateDsarTests | lab | main | yes | 1 | 0 | 0 | 0 |
| AcHcm21RequisitionApprovalUiTests | lab | main | yes | 4 | 0 | 0 | 12 |
| AcHcm22RecruitmentWorkflowsUiTests | lab | main | yes | 5 | 0 | 0 | 24 |
| AcHcm25ProductionApprovalTests | prodLike | main | yes | 8 | 0 | 0 | 41 |
| AcHcm26AssessmentInterviewTests | lab | isolated | yes | 5 | 0 | 0 | 135 |
| AcHcm27OfferMeritTests | lab | isolated | yes | 4 | 0 | 0 | 275 |
| AcHcm28CommitteeAppealTests | lab | main | yes | 3 | 1 | 0 | 80 |
| AcHcm29HireContractTests | lab | main | yes | 3 | 0 | 0 | 32 |
| AcHcm30CandidateExperienceTests | lab | main | yes | 8 | 0 | 0 | 338 |
| AcHcm31CompetitionTests | lab | main | yes | 1 | 1 | 0 | 201 |
| AcHcm32RecruitmentReportsTests | lab | main | yes | 4 | 0 | 0 | 5 |
| AcInt37ProductionWiringTests | prodLike | isolated | yes | 6 | 2 | 0 | 142 |
| AcMsg40AtomicPublishingTests | prodLike | isolated | yes | 8 | 0 | 0 | 94 |
| AcOps38FirstBootTests | prodLike | leastpriv | yes | - | - | - | - |
| AcOps39KeycloakDataProtectionTests | prodLike | isolated | yes | 8 | 0 | 0 | 77 |
| AcQa34PushableBaselineTests | lab | meta | yes | - | - | - | - |
| AcQa35RemoteCiTests | lab | meta | yes | - | - | - | - |
| AcQa41AcceptanceBaselineTests | prodLike | final | yes | - | - | - | - |
| AcWf23WorkInboxUiTests | lab | main | yes | 6 | 0 | 0 | 3 |
| RecruitmentStatesFileTests | lab | main | yes | 1 | 0 | 0 | 0 |
| RecruitmentToHireJourneyE2ETests | lab | journey | yes | - | - | - | - |
| RecruitmentToHireJourneyProdLikeE2ETests | prodLike | journey | yes | - | - | - | - |

