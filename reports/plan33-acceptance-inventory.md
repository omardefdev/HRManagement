# PLAN-33 acceptance inventory (baseline, before)

- Date: 2026-10-29
- Baseline: `main@4342bb6` (branch `feature/plan-33-verification-docs` points at the same commit)
- Scope: every test method in `src/Tests/HRManagement.AcceptanceTests` at `HEAD`
- Plan: `docs/acceptance-plans/PLAN-33-hcm-verification-docs.md`. Its goal is honest acceptance verification of the recruitment path: requisition -> application -> assessment -> ranking -> offer -> acceptance -> hire.

All line numbers refer to the files as committed at `4342bb6` (`git show HEAD:<path>`). The working tree already has uncommitted PLAN-33 edits. For example, `Ac33`, `Ac37`, `AcEst24`, `AcHcm21` and `AcHcm22` now call `AcceptanceGate.RequireData`, and there are untracked `AcceptanceGate.cs`, `AcceptanceGateTests.cs` and `AcceptanceSkipPatternArchitectureTests.cs` files. Those edits shift line numbers and are **not** part of this baseline.

## Summary

| Measure | Count | Detail |
|---|---|---|
| Test files | 63 | `git grep` over `HEAD` |
| Test methods | **103** | 90 `[SkippableFact]`, 13 `[Fact]`, 0 theories |
| By group | AC-01..37: 38, AC-EST: 16, AC-HCM: 33, AC-WF: 2, infrastructure: 14 | AC-05 has 2 methods; AcHcm30 has 8 |
| Gating: none (static/unit `[Fact]`) | 13 | Coverage (2), EvidenceSummary (2), TenantConsistency (2), V2Catalog (7) |
| Gating: `RequireEnabled` only (`Skip.IfNot RUN_ACCEPTANCE`, `AcceptanceHarness.cs:19-20`) | 51 | Includes 3 methods whose assertions can be bypassed (Ac12, Ac20, Ac32) |
| Gating: `RequireEnabled` + real `Skip.If` data/environment gates | 33 | Ac27, Ac29, Ac30, AcHcm06, AcHcm26 (5), AcHcm27 (4), AcHcm28 (4), AcHcm29 (2), AcHcm30 (8), AcHcm31 (2), AcHcm32 (4) |
| Gating: `RequireEnabled` + **silent** `WriteResult(...)`, no failure | 6 methods / **9 occurrences** | Ac33 (1), Ac37 (1), AcEst24a (2), AcEst24e (1), AcHcm21 (2), AcHcm22 (2) |
| Silent occurrences that record **"Pass"** | 2 | Ac33 L26-29, Ac37 L30-33 |
| Silent occurrences that record **"Skip"** (xUnit still reports Passed) | 7 | AcEst24a ×2, AcEst24e ×1 (conditional), AcHcm21 ×2, AcHcm22 ×2 |
| Methods with no assertions | **0** | But 3 assert only a script exit code (Ac16, Ac18, Ac28), and in 6 methods the assertions sit behind a silent return or condition |
| HTML scraping for **data discovery** (picks the record to act on from rendered HTML) | **4** | Ac17 (Hire link), AcEst24a, AcHcm21, AcHcm22 (first Details href) |
| HTML scraping anywhere (discovery, tokens or assertions) | 11 | The 4 above + Ac31, AcEst18d, AcEst24e, AcWf23a, AcHcm32a/b/d |
| Live-gated methods with no evidence folder in git | **45** | AC-01..37: 27 (AC-22 and AC-28 exist on disk only, untracked and empty); AC-EST: 13; AC-HCM: 3 (HCM-06-DSAR, AC-HCM-21, AC-HCM-22); AC-WF: 2 |
| Evidence folder present but incomplete or stale | 7 | AC-16 (no result, script `ok=false`), AC-30 (`before.json` = `-1`), AC-37 (metadata only), AC-01/AC-02 (older test version), AC-34/AC-36 (contract-only "Pass" reason not in the current code) |
| Last live result: Pass | 38 | Of these, 2 are stale contract-only (AC-34, AC-36) and 2 come from an older test version (AC-01, AC-02) |
| Last live result: Fail or incomplete | 4 | AC-16, AC-30, AC-37 (incomplete evidence); `Ac27_f_g` (failed in the PLAN-32 regression run, `reports/plan32-progress.md:172`) |
| Last live result: Skipped | 1 | AC-HCM-30c (File.Storage unavailable) |
| Last live result: none recorded | 60 | Includes all 14 infrastructure tests |

### Recruitment path coverage (PLAN-33 target)

| Step | Real path (UI/API) | Shortcut used elsewhere |
|---|---|---|
| Requisition | AcHcm26a-e/f-g/i, AcHcm27, AcHcm28e-i/h, AcHcm29, AcHcm30f-g (created via UI) | Seeded requisitions: AcHcm28a-d (CMT1), AcHcm30 (CX), AcHcm31 (competition), AcHcm32 (reports) |
| Requisition SoD | AcHcm21 (silent, weak OR assertion, never live) | none |
| Application | AcHcm26a-e (internal UI), AcHcm30a-b-h (public citizen apply) | none |
| Assessment | AcHcm26a-e (interview + scores); AcHcm31b-g (exam marking) | Interview slot moved into the past by SQL (AcHcm26 L102) |
| Ranking | AcHcm26a-e (`AssignRank`), AcHcm28a-c-d (`ApplyCommitteeRanking`), AcHcm31b-g (merit allocation) | **Test-only SQL `UPDATE` of ranks** in AcHcm27 (L363-365), AcHcm28e-i/h (L475-477), AcHcm29 (L352), AcHcm30f-g (L317-318) |
| Offer | AcHcm27, AcHcm28h, AcHcm29, AcHcm30f-g, AcHcm31 (bulk) | AcHcm26h inserts an Issued OfferLetter by SQL (L234-236) |
| Offer SoD | AcHcm22 (silent, weak OR assertion, never live) | none |
| Acceptance | AcHcm30f-g (candidate OTP); AcHcm27f-g, AcHcm28h, AcHcm29 (HR on behalf, evidence file) | none |
| Hire | AcHcm27f-g, AcHcm28h, AcHcm29; Ac17 (scrapes an existing shortlisted application) | AcHcm29 inserts ContractSignatures/CommencementRecords and moves ServiceTransactions by SQL (L127-131, L146-149) |

**No single test runs the whole path** (requisition -> application -> assessment -> real ranking -> offer -> acceptance -> hire) without a SQL shortcut. Every test that reaches hire injects ranks by SQL. The only tests with real ranking (AcHcm26a-e, AcHcm28a-c-d, AcHcm31b-g) do not continue to hire. `Ac15CorrelationIdHireJourneyTests` despite its name runs no recruitment step: it publishes a synthetic `IEmployeeCreated`.

## Silent patterns

`EvidenceRecorder.WriteResult` (`EvidenceRecorder.cs:91-96`) only writes `result.json` and appends to `summary.md`. It never throws or skips. So `WriteResult("Pass"|"Skip") + return` always gives an xUnit **Passed** outcome. The only real skips are `AcceptanceEnv.RequireEnabled` (`AcceptanceHarness.cs:19-20`) and the `Skip.If` gates listed in the tables.

### A. Silent `WriteResult` + `return` (xUnit reports Passed)

| # | Location (HEAD) | Condition | Recorded | Path relevance |
|---|---|---|---|---|
| 1 | `Ac33AssetLossLiveTests.cs:26-29` | `resp.StatusCode == NotFound` on `GET ui/admin-logistics/Assets/Details/{AcceptanceAssetBr1Id}` | **Pass** "Acceptance asset not seeded in this environment ..." (L28), `return` (L29) | Not recruitment |
| 2 | `Ac37JourneyRebuildLiveTests.cs:30-33` | `journeyResp.StatusCode == NotFound` on `GET api/platform/audit/journey/{correlationId}` | **Pass** "No audit rows yet for synthetic correlation ..." (L32), `return` (L33) | Not recruitment |
| 3 | `AcEst24EstablishmentWorkflowUiTests.cs:24-27` | `listResp.StatusCode != OK` on `GET ui/hr-core/EstablishmentChanges?status=PendingApproval` | Skip (L26), `return` (L27) | Establishment |
| 4 | `AcEst24EstablishmentWorkflowUiTests.cs:31-34` | `detailsHref is null` (HTML scrape) | Skip (L33), `return` (L34) | Establishment |
| 5 | `AcEst24EstablishmentWorkflowUiTests.cs:68-70` | `body.Contains("EstablishmentChanges/Details")` | `WriteResult(cond ? "Pass" : "Skip")`; the deep link is never asserted | Establishment |
| 6 | `AcHcm21RequisitionApprovalUiTests.cs:25-28` | `listResp.StatusCode != OK` on `GET ui/hr-core/RecruitmentRequisitions?status=PendingApproval` | Skip (L27), `return` (L28) | **Recruitment: requisition SoD** |
| 7 | `AcHcm21RequisitionApprovalUiTests.cs:32-35` | `detailsHref is null` (HTML scrape) | Skip (L34), `return` (L35) | **Recruitment: requisition SoD** |
| 8 | `AcHcm22RecruitmentWorkflowsUiTests.cs:25-28` | `listResp.StatusCode != OK` on `GET ui/hr-core/OfferLetters?status=PendingApproval` | Skip (L27), `return` (L28) | **Recruitment: offer SoD** |
| 9 | `AcHcm22RecruitmentWorkflowsUiTests.cs:32-35` | `detailsHref is null` (HTML scrape) | Skip (L34), `return` (L35) | **Recruitment: offer SoD** |

### B. Related: assertion bypass or weak acceptance (still records Pass)

| Location (HEAD) | Pattern |
|---|---|
| `Ac12PayrollSodTests.cs:58-74` | The approver-post denial is asserted only `if (submitResp.IsSuccessStatusCode)` and `if (approveResp.IsSuccessStatusCode)`. Otherwise execution falls through to `WriteResult("Pass")` at L83. |
| `Ac20DatabaseIsolationTests.cs:23-29, 32-38` | Log and cross-db assertions run only `if (File.Exists(...))`. Otherwise only `Assert.Equal(0, exitCode)` (L40) gates `WriteResult("Pass")` (L41). |
| `Ac32LogisticsScopeLiveTests.cs:36-37, 44-45` | The "allowed" br1 and central checks accept `NotFound`, so with an unseeded asset only the br2 403 is proven. |
| `AcHcm21...cs:45-48`, `AcHcm22...cs:44-47` | `Assert.True(contains(creator message) \|\| !contains(">اعتماد<"))`: this passes on any page that lacks the literal approve label. |
| `AcHcm30CandidateExperienceTests.cs:166-171` | A real `Skip.If(true)`, but only after `WriteDbChecks` and `FlushTiming`, with no `WriteResult`. It leaves partial evidence (`db-checks-30c.json` = "upload failed"). The clean-scan and EICAR steps have never run live. |
| `AcEst24...cs:68-70`, `AcWf23WorkInboxUiTests.cs:24-25` | They assert only the page status and title. The deep link or actionable items named in the method are never asserted. |

### C. Evidence semantics

`EvidenceRecorder.WriteMergedResult` (`EvidenceRecorder.cs:201-226`) overwrites `result.json` per AC folder. So `acceptance-evidence/AC-HCM-2x/result.json` shows only the **last** sub-scenario run (26i, 27f-g, 28b, 29a-h, 30a-b-h, 31b-g, 32c). Per-sub-scenario outcomes exist only in the append-only `acceptance-evidence/summary.md` and in `AC-HCM-27/28 run-summary.md`. A later live failure that was restored rather than committed (Ac27_f_g in PLAN-32) is therefore invisible in the committed evidence.

## Inventory tables

Column key: **Gating** "RE" = `AcceptanceEnv.RequireEnabled()` -> `Skip.IfNot(RUN_ACCEPTANCE=true)`. "Lxx" = line at HEAD. Evidence folder paths are under `acceptance-evidence/`.

### AC-01..37 (38 methods)

| Scenario | Class.Method | Attribute | Gating | Assertions | Data source | Evidence folder | Last live result | Notes |
|---|---|---|---|---|---|---|---|---|
| AC-01 | Ac01CentralTenantIsolationTests.Central_user_sees_agency_a_employees_not_agency_b (L12) | SkippableFact | RE L14. A missing password or secret causes a hard `Assert.Fail` (AcceptanceHarness.cs:65,112) | yes (+ helpers AssertTenantClaim, EnsureNotServerError) | Seed ids EmpBr1Id/EmpBr2Id/EmpBId/AgencyAId; persona central-a | AC-01: tracked, 10 files | Pass (c7dba097…; captured 2026-08-24T17:48:09Z, gitSha 9cb3705) | Steps text (L26) says list search, but the code does per-id GETs (L41-55). Evidence `lookup-employees.json` does not match the current `lookup-{id:N}.json`, so it came from an older version. The db-checks are hardcoded ids (L62-66). |
| AC-02 | Ac02BranchManagerScopeTests.Br1_manager_sees_branch1_employees_not_sibling_branch2 (L12) | SkippableFact | RE L14 | yes (+ EnsureNotServerError) | Seed ids EmpBr1Id, EmpD1Id, EmpBr2Id, Br1/Br1D1 org units; br1-mgr | AC-02: tracked | Pass (cd60464c…; 2026-08-24, 9cb3705) | Evidence comes from an older test version. The sibling check accepts 403 or 404 (L46). The L58 `DoesNotContain` is tautological. |
| AC-03 | Ac03SiblingAccessDeniedTests.Br1_manager_denied_sibling_employee_via_api (L12) | SkippableFact | RE L14 | yes | Seed ids EmpBr2Id, AgencyAId, Br1OrgUnitId | AC-03: tracked | Pass (272958eb…; 2026-08-24, 9cb3705) | `audit.json` (L29-38) is built from constants. Accepts 403 or 404. |
| AC-04 | Ac04CrossTenantGuessTests.Tenant_a_user_cannot_guess_tenant_b_employee (L12) | SkippableFact | RE L14 | yes | Seed ids EmpBId, AgencyAId | AC-04: tracked | Pass (d523484c…; 2026-08-24, 9cb3705) | The db-checks "layers" (L28-39) are hardcoded prose. A single 404/403 is asserted. |
| AC-05 | Ac05PlatformEnterTenantTests.Platform_admin_enters_tenant_with_reason_then_can_query (L12) | SkippableFact | RE L14 | yes | Seed AgencyAId; platform-admin | AC-05: tracked (shared with the negative method) | Pass (c1742d9b…; 2026-08-24, 9cb3705) | No tenant-scoped query runs after enter, only exit (L43-46). `audit.json` is built from constants. |
| AC-05 (negative) | Ac05PlatformEnterTenantTests.Tenant_user_cannot_call_enter_tenant (L52) | SkippableFact | RE L54 | yes | Seed AgencyAId; br1-mgr | AC-05 (same folder, correlationId suffix `-negative`, L57) | Pass per summary.md (670589c1…-negative); result.json was overwritten by the positive method | Accepts 403, 401 or 405. |
| AC-06 | Ac06TransferAccessTests.Branch_managers_see_only_their_branch_employees (L11) | SkippableFact | RE L13 | yes | Seed ids EmpBr1Id, EmpBr2Id, Br1/Br2 org units | none | none | No transfer is executed. The EndDate closure is only claimed in the db-checks text (L51-55). It proves static branch scoping only. |
| AC-07 | Ac07AgencyOnboardingTests.Apply_twice_is_idempotent_with_org_tree (L12) | SkippableFact | RE L14 | yes | `seed-packs/examples/agency-acceptance-test.json` applied via API | none | none | The org tree is counted from the manifest (L59), not the DB. The first apply asserts `alreadyApplied=false`, so a rerun fails. |
| AC-08 | Ac08QuarantineReconciliationTests.Quarantined_employee_blocked_then_resolved (L14) | SkippableFact | RE L16 | yes | Runtime, via `acceptance-quarantine-flow.ps1` + seed AgencyAId | none | none | The reconcile before/after responses are captured but not asserted (L32-35, L68-70). |
| AC-09 | Ac09ShadowPolicyTests.Shadow_enforce_keeps_allowed_true_and_writes_ledger (L14) | SkippableFact | RE L16 | yes | Seed PolicyVersion2Id, AgencyAId | none | none | The ledger check is the substring "Shadow" (L74). The db-checks are hardcoded booleans. |
| AC-10 | Ac10EnforcePolicyTests.Publish_v3_closes_v2_and_enforce_applies_deny (L14) | SkippableFact | RE L16 | yes | Seed PolicyVersion2Id, PolicyVersion3DraftId, AgencyAId | none | none | Mutates state (publishes v3), so it is not re-runnable. Uses `forceSameActorForLab=true`. The ledger is not asserted. `v2EffectiveToClosedOnPublish` is hardcoded. |
| AC-11 | Ac11PolicyWorkflowTests.Enforce_starts_workflow_two_approvals_signatures_stage_skip_blocked (L14) | SkippableFact | RE L16 | yes | Mixed: seed AgencyBId policy; runtime assignment and workflow | none | none | The ledger is captured but not asserted. |
| AC-12 | Ac12PayrollSodTests.Payroll_SoD_blocks_creator_approve_and_approver_post (L12) | SkippableFact | RE L14; **conditional assert bypass L58-74** | yes (partially conditional) | Runtime payroll run 2027-06 | none | none | If submit or approve fails, only the self-approve 403 is proven, and the test still records Pass (L83). The fixed period may collide on rerun. |
| AC-13 | Ac13ParallelCodeGenerationTests.Parallel_generate_produces_unique_asset_tags (L14) | SkippableFact | RE L16 | yes | Runtime (120 generations, seed AgencyAId) | none | none | |
| AC-14 | Ac14TemporalOrgStructureTests.AsOf_before_rename_unchanged_after_structural_update (L12) | SkippableFact | RE L14 | yes | Seed Br1OrgUnitId, EmpBr2Id; mutates the org unit | none | none | The L72 `NotEqual` fails on a second run. The head employee is not verified. |
| AC-15 | Ac15CorrelationIdHireJourneyTests.Hire_journey_preserves_correlation_id_across_hops (L11) | SkippableFact | RE L13 | yes | Lookup on seed data + synthetic `IEmployeeCreated` to RabbitMQ (L38-40) + `acceptance-correlation-trace.ps1` | none | none | **No recruitment or hire happens** despite the name. L50 `GetProperty("foundCount")` throws if the key is missing. |
| AC-16 | Ac16DuplicateMessageTests.Duplicate_IEmployeeCreated_single_auth_user_and_receipt (L9) | SkippableFact | RE L11 | yes (script exit code only, L36) | Runtime synthetic message ×2 + `acceptance-inbox-duplicate.ps1` | AC-16: tracked, `db-checks.json` only | **Fail/incomplete**: no result.json; db-checks `ok=false`, counts -1 (2026-08-25T00:40:35Z) | The receipt and user counts are not asserted in C#. Uses a fixed 5 s delay. |
| AC-17 | Ac17OutboxSurvivesRabbitDownTests.Outbox_survives_rabbit_stop_and_drains_on_restart (L11) | SkippableFact | RE L13; hard fail if no Hire link (L37) | yes (+ messaging/psql helper asserts) | **HTML scraping**: first Hire link in RecruitmentApplications (L33-38) + antiforgery and ApplicationId regex (L44-45, L138-146); psql counts | none | none | The only AC-01..37 test that drives hire. It consumes a pre-existing shortlisted application and runs docker stop/start on RabbitMQ. The drain check is global (L112). Requisition, assessment, ranking, offer and acceptance are not verified. |
| AC-18 | Ac18ConsumerCatchUpTests.Auth_directory_catch_up_after_consumer_stop (L9) | SkippableFact | RE L11 | yes (script exit code only, L36) | Runtime synthetic message + `acceptance-consumer-catchup.ps1` | none | none | All verification is in the script. Uses a fixed 4 s delay. |
| AC-19 | Ac19AnalyticsReconciliationTests.Reconcile_detects_corrects_and_idempotent_second_run (L11) | SkippableFact | RE L13 | yes | Runtime via `acceptance-analytics-reconcile.ps1` | none | none | Persona a-analytics. |
| AC-20 | Ac20DatabaseIsolationTests.Verify_postgres_service_roles_isolation (L10) | SkippableFact | RE L12; **conditional asserts L23, L32** | yes (partially conditional) | `acceptance-verify-db-isolation.ps1` | none | none | Passes on exit code alone if the script emits no evidence files. |
| AC-21 | Ac21PayrollDeterminismTests.Dual_run_provenance_and_determinism_diff_empty (L10) | SkippableFact | RE L12 | yes | `acceptance-payroll-determinism.ps1` (period 2027-01) | none | none | Sets the process-wide `ACCESS_TOKEN` and `BASE_URL`. |
| AC-22 | Ac22PayrollResumeAfterKillTests.Resume_after_kill_completes_without_duplicate_lines (L10) | SkippableFact | RE L12 (`RUN_CHAOS` is recorded only, not a gate) | yes | `acceptance-payroll-resume-chaos.ps1` writes result.json | On disk only, empty `db-checks/` (untracked) | none | The script and `WriteResult` share result.json (merged by `EvidenceRecorder.cs:201-226`). |
| AC-23 | Ac23PayrollFreezeTests.Frozen_run_isolated_from_profile_edit (L12) | SkippableFact | RE L14 | yes | Runtime runs 2027-02 and 2027-03 | none | none | The profile edit status is not asserted (L59-71). |
| AC-24 | Ac24PartialDisbursementTests.Partial_disbursement_http_path_invariant_and_idempotency (L12) | SkippableFact | RE L14 | yes (+ RequireSuccessAsync) | Runtime run 2027-04 | none | none | |
| AC-25 | Ac25FraudHoldTests.Fraud_hold_blocks_pay_until_four_eyes_release (L13) | SkippableFact | RE L15 | yes (+ RequireSuccessAsync, RequireSubject) | Mixed: seed EmpBr1Id pay profile overwritten; run 2027-05; audit fixture script | none | none | Mutates the seed pay profile. |
| AC-26 | Ac26NationalIdPrivacyTests.Hash_search_and_role_based_display (L14) | SkippableFact | RE L16 | yes | Seed NationalIdBr1 + `ac026-scan-pii.ps1` | none | none | The "details" call (L39-45) repeats the hash search. |
| AC-27 | Ac27DsarExportTests.Fulfill_produces_masked_package_without_raw_national_id (L15) | SkippableFact | RE L17; `Skip.If` create 404 (L35) | yes | Seed EmpBr1Id; DSAR created at runtime | none | none | L34 deserializes before the skip, so a non-JSON 404 body throws. |
| AC-28 | Ac28FileScanTests.Live_file_scan_script_produces_evidence (L14) | SkippableFact | RE L16 | yes (script exit code only) | `acceptance-file-scan.ps1` | On disk only, empty (untracked) | none | All verification is in the script. |
| AC-29 | Ac29FileAccessExpiryTests.Presign_valid_for_uploader_wrong_user_denied (L14) | SkippableFact | RE L16; `Skip.If` upload 404 (L34) | yes | Runtime PDF upload | none | none | Expiry is not tested. Polls up to 45 s for Clean. |
| AC-30 | Ac30S2sFileReferenceRejectionTests.Rogue_service_token_rejected_without_new_reference (L13) | SkippableFact | RE L15; `Skip.If` upload 404 (L27) | yes (upload status + script exit code) | Runtime upload + `acceptance-s2s-negative.ps1` | AC-30: tracked, `before.json` only | **Incomplete**: no result.json; before.json is `-1` | The no-side-effect check is in the script. |
| AC-31 | Ac31MassAssignmentTests.Inventory_create_ignores_hidden_tenant_and_audit_fields (L13) | SkippableFact | RE L15 | yes (+ psql helper) | **HTML scraping** (token L36, result cell L70-73) + runtime row + psql | none | none | CreatedAt and ApprovedBy are not verified. |
| AC-32 | Ac32LogisticsScopeLiveTests.Br2_manager_denied_br1_custody_asset_details (L11) | SkippableFact | RE L13; **weak accept of 404** (L36-37, L44-45) | yes | Seed AcceptanceAssetBr1Id, EmpBr1Id | none | none | With an unseeded asset it passes on the br2 403 alone. |
| AC-33 | Ac33AssetLossLiveTests.Acceptance_asset_details_expose_loss_snapshot_fields_when_present (L11) | SkippableFact | RE L13; **SILENT Pass on 404** (L26-29) | yes (+ EnsureNotServerError) | Seed AcceptanceAssetBr1Id/Tag | none | none | 403 is also accepted (L33). Loss snapshots are never asserted; `WriteAfter` is a hardcoded note (L38-43). |
| AC-34 | Ac34BackupRestoreLiveTests.Backup_and_restore_drill_preserves_critical_data_and_relationships (L9) | SkippableFact | RE L11 | yes | `acceptance-backup-restore.ps1` -> drill-result.json | AC-34: tracked; **no drill-result.json** | "Pass" with a contract-only reason (18a850cb…; 2026-08-24T17:16:39Z) | **Stale**: the reason string is not in the current code. This is not a live drill pass. |
| AC-35 | Ac35ScopedLookupPerfLiveTests.Br1_manager_lookup_records_reproducible_gateway_percentiles (L11) | SkippableFact | RE L13 | yes (warm-up, zero errors; no SLO) | Seed data via lookup q=ACC | none | none | `sloClaimed=false`. |
| AC-36 | Ac36ArabicPdfLiveTests.Arabic_pdf_samples_are_generated_and_have_valid_pdf_headers (L9) | SkippableFact | RE L11 | yes | `acceptance-pdf-arabic.ps1` -> samples/ | AC-36: tracked; **no samples/ or checklist** | "Pass" with a contract-only reason (9d02ff78…; 2026-08-24T17:16:39Z) | **Stale**. Human visual sign-off is required, and the test checks only headers and sizes. |
| AC-37 | Ac37JourneyRebuildLiveTests.Platform_admin_rebuilds_journey_after_scoped_lookup (L11) | SkippableFact | RE L13; **SILENT Pass on 404** (L30-33) | yes | Runtime correlationId + br1 lookup on seed data | AC-37: tracked, metadata.json + empty http/ | **Incomplete**: no result.json (the run died before L22) | The br1 lookup status is never asserted. On success it checks only the substring "httpEvents". |

### AC-EST (16 methods)

| Scenario | Class.Method | Attribute | Gating | Assertions | Data source | Evidence folder | Last live result | Notes |
|---|---|---|---|---|---|---|---|---|
| AC-EST-10 | AcEst10EstablishmentGapTests.Gap_analysis_reflects_funded_counts_and_deficit (L11) | SkippableFact | RE | yes | Seed Br1OrgUnitId, ReferenceDate year; JSON API | AC-EST-10: tracked, 9 files | Pass 2026-09-04T16:06:11Z | funded = occupied + vacant + frozen; deficit > 0. |
| AC-EST-11 | AcEst11BranchGapScopeTests.Br1_manager_allowed_on_br1_forbidden_on_br2 (L11) | SkippableFact | RE | yes | Seed Br1/Br2 org units; JSON API | AC-EST-11: tracked | Pass 2026-09-04T16:06:12Z | |
| AC-EST-12 | AcEst12FinanceReconciliationTests.Import_csv_then_reconciliation_shows_variance (L12) | SkippableFact | RE | yes | Seed + CSV `br1-budget-sample.csv`; runtime import | AC-EST-12: tracked | Pass 2026-09-04T16:06:11Z | Stub finance import (`liveErp=false`). |
| AC-EST-13 | AcEst13EstablishmentMdmLookupTests.Reference_data_lookup_returns_national_grades (L10) | SkippableFact | RE | yes | Seed MDM lookup | none | none | Asserts only count > 0. |
| AC-EST-14 | AcEst14JobCatalogCodPreviewTests.Preview_job_catalog_code_returns_non_empty_value (L11) | SkippableFact | RE | yes | Hardcoded GR-05/JC-ADM; preview only | none | none | Asserts only that the code is non-empty. |
| AC-EST-15 | AcEst15JobCatalogBulkImportTests.Import_valid_csv_submit_applies_catalog_rows (L13) | SkippableFact | RE | yes (+ JobCatalogExistsAsync) | CSV `job-catalog-sample.csv`; runtime change request | none | none | `beforeHasManager` is recorded but not asserted false. |
| AC-EST-16 | AcEst16JobCatalogImportValidationTests.Import_invalid_grade_is_rejected (L11) | SkippableFact | RE | yes | CSV `job-catalog-invalid-grade.csv` | none | none | Negative test. |
| AC-EST-17 | AcEst17JobCatalogMigrationLegacyTests.Migration_mode_applies_legacy_job_code (L13) | SkippableFact | RE | yes | CSV `job-catalog-migration-legacy.csv`; runtime | none | none | |
| AC-EST-17 | AcEst17JobCatalogMigrationLegacyTests.Standard_mode_rejects_legacy_job_code_column (L63) | SkippableFact | RE | yes | Same CSV, Standard mode | none | none | Negative test. |
| AC-EST-18a | AcEst18ReferenceCatalogAdminTests.Central_creates_GR16_visible_in_grade_lookup (L13) | SkippableFact | RE | yes | Runtime (409 Conflict accepted) | none | none | A rerun does not prove the create. |
| AC-EST-18b | AcEst18ReferenceCatalogAdminTests.Central_creates_JcHr_visible_in_classification_lookup (L54) | SkippableFact | RE | yes | Runtime (409 accepted) | none | none | |
| AC-EST-18c | AcEst18ReferenceCatalogAdminTests.Branch_manager_forbidden_on_mvc_create (L88) | SkippableFact | RE | yes | br1-mgr; MVC GET | none | none | Asserts 403. |
| AC-EST-18d | AcEst18ReferenceCatalogAdminTests.Deactivate_grade_used_in_job_catalog_rejected (L106) | SkippableFact | RE | yes | Seed grade G5 + **HTML scraping** (token regex L138) | none | none | L156 re-GETs the details page. The real check is `isActive == true` (L161). |
| AC-EST-24a | AcEst24EstablishmentWorkflowUiTests.Establishment_details_shows_workflow_panel_for_pending_change (L10) | SkippableFact | RE; **SILENT Skip** L24-27 and L31-34 | yes (only after the silent gates) | **HTML scraping for discovery** (first Details href); pre-existing PendingApproval change | none | none | On an empty stack xUnit reports Pass. |
| AC-EST-24e | AcEst24EstablishmentWorkflowUiTests.WorkInbox_may_list_establishment_change_deep_link (L52) | SkippableFact | RE; **SILENT conditional status** L68-70 | yes (200 + title only) | HTML scraping (WorkInbox) | none | none | The deep link is never asserted. No `WriteMetadata`. |
| AC-EST-24f | AcEst24EstablishmentWorkflowUiTests.Enforce_capstone_pending_api_shape_when_stack_available (L74) | SkippableFact | RE | yes | API shape only (`workflowCount`) | none | none | Checks shape only. |

### AC-HCM (33 methods)

| Scenario | Class.Method | Attribute | Gating | Assertions | Data source | Evidence folder | Last live result | Notes |
|---|---|---|---|---|---|---|---|---|
| HCM-06 | AcHcm06CandidateDsarTests.Candidate_fulfill_produces_masked_package_without_raw_national_id (L15) | SkippableFact | RE; `Skip.If` create 404 (L35), fulfill 400 (L50) | yes | Seeded national id "AC-1-000001"; runtime DSAR | none (`HCM-06-DSAR` absent; `HCM-01/steps.md` is static) | none | Checks no 11-digit run, no codeHmac/requestIp, and a package hash. |
| AC-HCM-21 | AcHcm21RequisitionApprovalUiTests.Creator_details_page_does_not_expose_approve_action_for_own_requisition (L10) | SkippableFact | RE; **SILENT Skip** L25-28 and L32-35 | yes, **weak** (OR at L45-48) | **HTML scraping for discovery** (first pending requisition Details href); pre-existing data | **none** | **none** | Recruitment path (requisition SoD). It can pass without checking anything, and never verifies that central-a created the requisition. |
| AC-HCM-22 | AcHcm22RecruitmentWorkflowsUiTests.Offer_creator_details_page_blocks_self_approval (L10) | SkippableFact | RE; **SILENT Skip** L25-28 and L32-35 | yes, **weak** (OR at L44-47) | **HTML scraping for discovery** (first pending OfferLetter Details href); pre-existing data | **none** | **none** | Recruitment path (offer SoD). The committee and appeal UI named in the summary are not tested. |
| AC-HCM-26a-e | AcHcm26AssessmentInterviewTests.Ac26_a_b_c_d_e_full_selection_pipeline_without_seed_data (L31) | SkippableFact | RE; `Skip.If` no free BR1 position (L340) | yes (DB state via psql) | Runtime UI: requisition, criteria, application, interview, scores, `AssignRank`; SQL moves the slot into the past (L102) | AC-HCM-26: http/26a-e-*, steps-26a-e.md | Pass 2026-10-21 (summary.md) | **The only test that reaches Ranked through the real UI.** It stops before the offer. |
| AC-HCM-26f/g | AcHcm26AssessmentInterviewTests.Ac26_f_g_duplicate_requisition_and_cancel_cascade (L144) | SkippableFact | RE; L340 | yes | Runtime UI + SQL INSERT for the DB-guard negative (L165-170) | AC-HCM-26: http/26f-g-* | Pass 2026-10-21 (summary.md) | |
| AC-HCM-26g neg | AcHcm26AssessmentInterviewTests.Ac26_g_cancel_blocked_when_candidate_accepted_offer (L188) | SkippableFact | RE; `Skip.If` no matching seeded pipeline (L200) | yes | Seed pipeline found by SQL (status 4/9) | AC-HCM-26: http/26g-neg-* | Pass 2026-10-21 (summary.md) | Depends on whichever seed row matches. |
| AC-HCM-26h | AcHcm26AssessmentInterviewTests.Ac26_h_freeze_cascade_through_establishment_workflow (L212) | SkippableFact | RE; `Skip.If` no second central persona (L221); L340 | yes | Runtime + SQL INSERT of an Issued OfferLetter (L234-236) | **no 26h files** | none (skipped live; PLAN-26-EXECUTION.md:50) | The freeze cascade has never run live. |
| AC-HCM-26i | AcHcm26AssessmentInterviewTests.Ac26_i_intake_close_job_closes_expired_posting (L255) | SkippableFact | RE; L340 | yes | Runtime + SQL time travel + docker restart | AC-HCM-26: http/26i-*, result.json = 26i | Pass 2026-10-21T19:44:22Z | |
| AC-HCM-27a/c/d/h | AcHcm27OfferMeritTests.Ac27_a_c_d_h_merit_offer_terms_decline_next_candidate_and_revoke (L36) | SkippableFact | RE; `Skip.If` no graded position (L339); Payroll unreachable (L385) | yes | Runtime UI + **SQL ranks (L363-365)** + payroll_db grade bounds | AC-HCM-27: run-summary.md, steps, http | Pass 2026-10-22 (run-summary, 8c3afdf4…); passed again in the PLAN-32 regression | Ranking is SQL-injected. |
| AC-HCM-27b | AcHcm27OfferMeritTests.Ac27_b_exception_offer_needs_reason_and_three_distinct_approvers (L114) | SkippableFact | RE; `Skip.If` approver-2 missing (L118); L339; L385 | yes | Runtime UI + SQL ranks | AC-HCM-27: http/27b-* | Pass 2026-10-22 (run-summary) | |
| AC-HCM-27e | AcHcm27OfferMeritTests.Ac27_e_offer_expiry_job_and_expired_offer_cannot_be_accepted (L166) | SkippableFact | RE; L339; L385 | yes | Runtime UI + SQL ranks + SQL time travel + docker restart | AC-HCM-27: http/27e-* | Pass 2026-10-22 (run-summary) | Uses evidence PDF. |
| AC-HCM-27f/g | AcHcm27OfferMeritTests.Ac27_f_g_hire_moves_ranked_siblings_to_reserve_list_which_expires (L205) | SkippableFact | RE; L339; L385 | yes | Runtime UI (offer, on-behalf accept with evidence PDF, Hire) + SQL ranks + time travel + restart | AC-HCM-27: http/27f-g-*, result.json = 27f-g | **Fail** in the PLAN-32 regression: offer stays Issued at the HR paper acceptance because File.Storage was down (`reports/plan32-progress.md:172`, `reports/plan32-final-report.md:140`). Committed evidence still shows Pass 2026-10-22T06:42:03Z. | Offer, acceptance and hire. L226 `AcceptanceEvidenceFileId` needs File.Storage. The failed run's evidence was restored, not committed. |
| AC-HCM-28a/c/d | AcHcm28CommitteeAppealTests.Ac28_a_c_d_committee_votes_decisions_three_step_approval_minutes_and_ranking_gate (L38) | SkippableFact | RE; `Skip.If` session already submitted (L42, single-use seed) | yes | Seed CMT1 requisition and session + runtime votes and approval | AC-HCM-28: run-summary.md, http/28a-c-d-* | Pass 2026-10-22 (run-summary) | Real committee ranking (`ApplyCommitteeRanking`). No offer or hire. |
| AC-HCM-28b | AcHcm28CommitteeAppealTests.Ac28_b_duplicate_member_and_session_creator_approval_are_refused (L138) | SkippableFact | RE; `Skip.If` no Assessed/Ranked candidate (L153) | yes | Seed CMT1 + runtime session | AC-HCM-28: http/28b-*, result.json = 28b | Pass 2026-10-22T12:03:21Z | |
| AC-HCM-28e/f/g/i | AcHcm28CommitteeAppealTests.Ac28_e_f_g_i_single_appeal_path_with_independent_compliance_reviewer (L206) | SkippableFact | RE; `Skip.If` a-compliance-2 missing (L210); no graded position (L449) | yes | Runtime UI + **SQL ranks (L475-477)** + time travel (L304) | AC-HCM-28: http/28e-f-g-i-* | Pass 2026-10-22 (run-summary, second run) | |
| AC-HCM-28h | AcHcm28CommitteeAppealTests.Ac28_h_hire_is_blocked_while_an_appeal_on_the_requisition_is_pending (L334) | SkippableFact | RE; L449; Payroll unreachable (L561) | yes | Runtime UI (offer, on-behalf accept with evidence, Hire) + SQL ranks | AC-HCM-28: http/28h-* | Pass 2026-10-22 (run-summary) | Offer, acceptance and hire. Predates the PLAN-30 J evidence rule. |
| AC-HCM-29a-h | AcHcm29HireContractTests.Ac29_a_b_c_d_f_g_h_hire_contract_terms_activation_decision_and_offer_letter (L41) | SkippableFact | RE; no graded position (L315); Payroll unreachable (L364) | yes | Runtime UI + **SQL ranks (L352)**, SQL NationalId (L78), SQL INSERT signatures/commencement (L127-131), SQL ServiceTransactions (L146-149) | AC-HCM-29: steps, db-checks, http/29a-h-* | Pass 2026-10-22T17:32:03Z (result.json) | Hire -> contract -> pay profile, with many SQL shortcuts. The decision prefix is not asserted (known COD defect, L161-163). |
| AC-HCM-29e | AcHcm29HireContractTests.Ac29_e_unresolved_contract_type_opens_an_intervention_and_manual_contract_clears_it (L181) | SkippableFact | RE; L315; L364; `Skip.IfNot` seeded contract type (L223) | yes | Runtime UI + SQL ranks | AC-HCM-29: steps-29e, db-checks-29e, http | Pass 2026-10-22 (summary.md) | |
| AC-HCM-29i | AcHcm29HireContractTests.Ac29_i_internal_mobility_message_opens_no_appointment (L242) | SkippableFact | RE only | yes (+ WaitForTextAsync `Assert.Fail`) | Synthetic `ICandidateAccepted` v4 over AMQP | AC-HCM-29: steps-29i, http | Pass 2026-10-22 (summary.md) | Messaging only; no UI path. |
| AC-HCM-30a/b/h | AcHcm30CandidateExperienceTests.Ac30_a_b_h_public_apply_rejection_status_and_candidate_appeal (L65) | SkippableFact | RE; `Skip.If` CX requisition not seeded (L671) | yes | Seed CX requisition/posting + anonymous apply; OTP from FileDrop; SQL ids | AC-HCM-30: steps, db-checks | Pass 2026-10-29T04:40:08Z (result.json) | Public application path. |
| AC-HCM-30c | AcHcm30CandidateExperienceTests.Ac30_c_documents_are_scanned_refused_files_and_eicar_is_quarantined (L130) | SkippableFact | RE; L671; `Skip.If(true)` on upload failure (L166-171) after partial evidence, with no result | yes | Seed CX + byte fixtures (exe, >5 MB, clean PDF, EICAR) | AC-HCM-30: db-checks-30c = "upload failed" | **Skipped** 2026-10-29 (MinIO image not pullable) | The clean scan and EICAR quarantine have never been verified live. |
| AC-HCM-30d/e | AcHcm30CandidateExperienceTests.Ac30_d_e_eligibility_flags_interview_message_once_and_no_hire_step (L215) | SkippableFact | RE; L671 | yes | Seed CX + runtime apply/screen/schedule; SQL (L269); FileDrop | AC-HCM-30: steps-30d-e, db-checks | Pass 2026-10-29 (summary.md) | Uses a fixed 70 s delay. |
| AC-HCM-30f/g | AcHcm30CandidateExperienceTests.Ac30_f_g_candidate_declines_then_next_candidate_accepts_with_otp (L292) | SkippableFact | RE; no graded position (L823); Payroll unreachable (L858, L879) | yes | Runtime UI requisition + citizen applies + **SQL ranks (L317-318)** + FileDrop OTP | AC-HCM-30: steps-30f-g, db-checks | Pass 2026-10-29 (summary.md) | Candidate-side acceptance. Stops at OfferAccepted (no hire). |
| AC-HCM-30f neg | AcHcm30CandidateExperienceTests.Ac30_f_wrong_codes_lock_the_portal_and_another_persons_identity_gets_no_access (L367) | SkippableFact | RE; L671 | yes | Seed CX + runtime applies + OTP | AC-HCM-30: steps/db-checks-30f-negative | Pass 2026-10-29 (summary.md) | |
| AC-HCM-30h neg | AcHcm30CandidateExperienceTests.Ac30_h_appeal_after_the_window_shows_no_button_and_the_post_is_refused (L434) | SkippableFact | RE; L671 | yes | Seed CX + runtime apply + SQL time travel (L451) | AC-HCM-30: steps/db-checks-30h-negative | Pass 2026-10-29 (summary.md) | |
| AC-HCM-30i | AcHcm30CandidateExperienceTests.Ac30_i_retention_anonymizes_the_due_pair_releases_files_and_keeps_the_held_pair (L470) | SkippableFact | RE; `Skip.If` fixtures missing (L491) | yes | Seed retention ids | AC-HCM-30: steps/db-checks-30i | Pass 2026-10-29 (summary.md) | Accepts prior anonymization by the startup job. |
| AC-HCM-30j | AcHcm30CandidateExperienceTests.Ac30_j_backfill_links_unlinked_citizen_and_internal_rows_once (L533) | SkippableFact | RE; `Skip.If` fixtures missing (L553) or already linked (L559) | yes | Seed backfill ids + `hcm030-backfill-unified-intake.ps1` | AC-HCM-30: steps/db-checks-30j | Pass 2026-10-29 (summary.md) | Single-use fixture. |
| AC-HCM-31a | AcHcm31CompetitionTests.Ac31_a_competition_groups_requisitions_is_approved_by_others_and_announced (L39) | SkippableFact | RE; `Skip.If` seeded requisitions missing or attached (L45) | yes | Seed competition requisitions + runtime competition | AC-HCM-31: http/31a-* | Pass 2026-10-29 (summary.md) | |
| AC-HCM-31b..g | AcHcm31CompetitionTests.Ac31_b_to_g_exam_results_objection_allocation_bulk_offers_and_reserve_recall (L122) | SkippableFact | RE; `Skip.If` L126, L243, L302, L322 | yes | Seed competition (120 applicants) + runtime marking/allocation + SQL time travel (L278) | AC-HCM-31: http/31b-g-*, result.json = 31b-g | Pass 2026-10-29T11:59:14Z | Real merit allocation and bulk offers. Mid-journey `Skip.If` (L243, L322) can skip after the seed has been mutated. |
| AC-HCM-32a | AcHcm32RecruitmentReportsTests.Ac32_a_funnel_and_time_to_fill_match_the_seed (L28) | SkippableFact | RE; `Skip.If` fixtures != 3 (L158) | yes | Seed reports patch + HTML scraping (report tables) | AC-HCM-32: http/32a-* | Pass 2026-10-29 (summary.md) | Read-only. Figures come from the seed, not a live journey. |
| AC-HCM-32b | AcHcm32RecruitmentReportsTests.Ac32_b_branch_manager_sees_only_its_unit (L56) | SkippableFact | RE; L158 | yes | Seed + HTML scraping | AC-HCM-32: http/32b-* | Pass 2026-10-29 (summary.md) | |
| AC-HCM-32c | AcHcm32RecruitmentReportsTests.Ac32_c_exports_have_no_national_id_and_no_names_for_read_only (L87) | SkippableFact | RE; L158 | yes | Seed + xlsx/CSV parse | AC-HCM-32: http/32c-*, result.json = 32c | Pass 2026-10-29T13:29:03Z | |
| AC-HCM-32d | AcHcm32RecruitmentReportsTests.Ac32_d_overdue_approval_is_listed (L132) | SkippableFact | RE; L158; `Skip.If` workflow not InProgress (L137) | yes | Seed workflow instance + HTML scraping | AC-HCM-32: http/32d-* | Pass 2026-10-29 (summary.md) | |

### AC-WF (2 methods)

| Scenario | Class.Method | Attribute | Gating | Assertions | Data source | Evidence folder | Last live result | Notes |
|---|---|---|---|---|---|---|---|---|
| AC-WF-23a | AcWf23WorkInboxUiTests.WorkInbox_index_lists_actionable_items_for_hr_core_user (L10) | SkippableFact | RE | yes (200 + title "صندوق العمل", L24-25) | HTML page content | **none** | **none** | Does not check that any actionable item is listed, despite the name. |
| AC-WF-23e | AcWf23WorkInboxUiTests.Pending_approvals_api_returns_unified_count_shape (L31) | SkippableFact | RE | yes (JSON keys, L44-47) | API shape only | **none** | **none** | No `WriteMetadata`. |

### Infrastructure (14 methods)

| Scenario | Class.Method | Attribute | Gating | Assertions | Data source | Evidence folder | Last live result | Notes |
|---|---|---|---|---|---|---|---|---|
| n/a (AC-01..37 presence) | AcceptanceCoverageTests.All_thirty_seven_acceptance_scenarios_have_test_classes (L14) | Fact | none | yes | Reflection (`^Ac(\d{2})`) | n/a | n/a | Does not cover AC-HCM/EST/WF, and does not check that classes contain tests. |
| n/a (evidence contract) | AcceptanceCoverageTests.Evidence_recorder_supports_eight_file_contract (L34) | Fact | none | yes | Reflection over EvidenceRecorder | n/a | n/a | `Assert.Equal(8, required.Length)` (L53) is a tautology. |
| n/a (AC-98 unit) | AcceptanceEvidenceSummaryTests.Recorder_completes_contract_and_preserves_detailed_result (L6) | Fact | none | yes | Temp dir | n/a (temp, deleted) | n/a | |
| n/a (AC-99 unit) | AcceptanceEvidenceSummaryTests.Rebuild_creates_summary_markdown (L36) | Fact | none | yes | Temp dir | n/a (temp, deleted) | n/a | |
| n/a (identity population) | AcceptanceIdentityPopulationTests.All_55_personas_have_the_expected_login_and_claim_outcome (L8) | SkippableFact | RE | yes | `personas.json` + live Keycloak password grants | n/a (no recorder) | none | **Cannot pass live as written**: 60 personas (L12), 2 disabled, so 58 enabled. But it asserts `successful == 56` (L71) and zero failures (L73). The name says 55. |
| n/a (PLAN-00.1) | AcceptanceTenantConsistencyTests.Acceptance_seed_ids_match_personas_and_manifest (L11) | Fact | none | yes | Seed ids vs manifest.json and personas.json | n/a | n/a | |
| n/a | AcceptanceTenantConsistencyTests.Legacy_conflicting_tenant_ids_removed_from_manifest (L41) | Fact | none | yes | manifest.json text | n/a | n/a | |
| n/a (seed catalog) | AcceptanceV2CatalogTests.Canonical_topology_has_the_approved_non_uniform_totals (L10) | Fact | none | yes | AcceptanceSeedCatalog | n/a | n/a | RecruitmentApplications volume is 25. |
| n/a (seed catalog) | AcceptanceV2CatalogTests.Every_employee_and_org_unit_identifier_is_unique_and_tenant_consistent (L62) | Fact | none | yes | Seed catalog | n/a | n/a | |
| n/a (persona catalog) | AcceptanceV2CatalogTests.Persona_catalog_has_57_separated_accounts_and_real_employee_links (L83) | Fact | none | yes | AcceptancePersonaCatalog | n/a | n/a | The name says 57, but it asserts 60 personas and 58 enabled (L86, L90). This contradicts the identity population test (56). |
| n/a (SoD) | AcceptanceV2CatalogTests.Persona_roles_respect_every_configured_separation_of_duties_conflict (L107) | Fact | none | yes | Persona catalog + SodMatrixDefaults | n/a | n/a | |
| n/a | AcceptanceV2CatalogTests.Committed_persona_source_contains_no_password_values (L134) | Fact | none | yes | personas.json text | n/a | n/a | |
| n/a | AcceptanceV2CatalogTests.Outbox_and_inbox_baselines_are_intentionally_empty (L143) | Fact | none | yes | DemoIntegrationData builders | n/a | n/a | |
| n/a | AcceptanceV2CatalogTests.Cross_service_user_references_resolve_to_canonical_auth_users (L151) | Fact | none | yes | Seed catalog GUIDs | n/a | n/a | |

## Spot-check log (against `git show HEAD:<file>`)

| Row | Checked | Result |
|---|---|---|
| AcHcm21 | Silent gates L25-28 / L32-35, OR assertion L45-48 | Correct |
| Ac12 | Conditional asserts L58-74, `WriteResult("Pass")` L83 | Correct |
| AcEst18d | Token regex L138, re-GET L156, `isActive` L161 | Correct |
| AcceptanceIdentityPopulation | L12 = 60, L71 = 56, L72 = 2; personas.json has 60 usernames with 2 `"enabled": false` | Correct; the contradiction is confirmed |
| AcHcm27 f_g | L205 and L226 | Lines correct. **Last live result corrected**: it failed in the PLAN-32 regression run (`reports/plan32-progress.md:172`). The PLAN-33 doc line 17 that mentions it exists only in the uncommitted working-tree version of the plan. |
| Extra | Method totals | 103 methods at HEAD; AcHcm30 has 8 and AcceptanceV2Catalog has 7 (matches the rows) |
| Extra | Duplicate silent-pattern entries | Merged. The Ac33 entry quoted as "25-29" is L26-29 at HEAD. |
