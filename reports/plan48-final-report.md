# PLAN-48 — final report: national settings for every agency, the agency ceiling, and sound numbering in every tenant

Branch `feature/plan-48-ten-national-agency-scope` (from `origin/main` 61816ed7dda2), released through `release/plan-48`.
Specification: `docs/acceptance-plans/execution/PLAN-48-AGENT-PROMPT.md` (it wins over the plan). Progress, decisions and every
test run: `reports/plan48-progress.md`. Baseline: `reports/plan48-baseline.md`. Ceiling impact: `reports/plan48-ceiling-impact.md`.

## 1. Summary per phase

| Phase | Result |
|-------|--------|
| P0 | Preconditions passed; baseline (build 61 s, unit filter 437/437, Testcontainers 19/19); inventories (national tables and row security, readers and writers, code generation calls, overlays under the ceiling, the server's codes, counters and tenants). The demo server already ran PLAN-47's last fix (61816ed7dda2). |
| P1 | The ten national tables marked `INationalShared`, one list (`NationalTenant.TableNames`); one read path (`OwnOrNational`, `NationalOnly`, `OwnFirst`) replacing all 20 `PolicyResolution`/`NationalReferenceLookup` bypasses; `NationalSharedWriteGuard`; `national_shared_read` (PERMISSIVE FOR SELECT) applied and checked on both migration paths and by the VPS bootstrap. Proved as a non-owner role under FORCE. |
| P2 | Code policies resolve overlay → national → none through the national read; `MissingPolicy=Refuse` outside Development/Testing; a missing fact is a 422, a collision a 409; `NationalScopeErrorFilter`; `/CodePolicies` shows inherited national templates; agency code overlays validated (`"default": true`). |
| P3 | `CodeFormat`, `CodeScopeKey`, `CodePolicyValidator`; `CodePolicyUpgrades` (FundedPosition and RecruitmentRequisition v1 → v2, once, recorded); `CodeCounterReseeder` (every tenant from `Tenants`, `GREATEST`, unparsed reported); derived cost centre; bootstrap rebuilt on the same parser and key. |
| P4 | `PolicyRuleDefinition.Origin`, `PolicyCeiling` at evaluation (drop + explanation) and at publish (PLAN-47's validator, through the template's scope); the orchestrator reads the national template through `NationalOnly` (the G-POL-05 fix); `CreateDraft` scope rules (403 for `Scope=National` outside the national tenant, page and `POST api/policies/drafts`); agency overlay packs through the validator, the ceiling and the publish service; the ceiling impact report. |
| P5 | Persona `platform-central`; AC-13 rebuilt (required); `AcTen48NationalScopeTests` (a..g) and `AcTen48LeastPrivilegeTests`; CI scope and ownership; local prod-like, lab and least-privilege runs green; one independent review (9 findings) and an adversarial verification workflow (5 agents); fixes. |
| P6 | `origin/main` had not moved: no merge. |
| P7 | Verified backup; one detached build on the server; deploy of 8abd5aa7fe93 on the first attempt; the eight checks green (funded positions on the demo recorded as a deviation). |
| P8 | Docs, local CI and the pre-push check, this report, the PR and the merge (results in the merge note of `reports/plan48-progress.md`). |

## 2. Commits

| Commit | Message |
|--------|---------|
| `1405ccd1`, `dbd19381` | PLAN-47's merge note (cherry-picked, P-1) |
| `ac5629e8` | docs(ten048): the PLAN-48 agent prompt |
| `692eeba3` | P0 chore(ten048): baseline, … |
| `e61ff9c8` | P1 feat(ten048): national settings are read through one path … |
| `2f3214c4` | P2 feat(ten048): every tenant sees the national code policies … |
| `8f41f5ce` | P3 feat(ten048): code policies are validated … |
| `e1ac13c1` | P4 feat(ten048): an agency inherits the national policy … |
| `8abd5aa7` | P5 test(ten048): AC-TEN-48a..h and AC-13 pass … review fixes; CI scope and ownership updated |
| `b83f0d74` | P7 test(ten048): demo server on national visibility and sound numbering … |
| P8 | docs(ten048) commit with the documents and this report; the `release/plan-48` merge commit (`--no-ff`) |

## 3. Decisions applied and deviations

Owner decisions D-TEN-1..10 and D-POL-4 applied as written. Decisions taken during the plan (reasons in the progress report):

- **D-P0-1 (deviation):** citizen references stay one national sequence issued in the national tenant (`CitizenApplications.ReferenceNumber` is unique across tenants and the portal finds an application by the reference alone). The prefix defect is fixed either way.
- **D-P0-2 (deviation):** AC-13's third kind is `ArchiveReference` (svc-records-archive, two prefixes), because `RecruitmentRequisition` has no service owner on PLAN-42's service path; the requisition's budget-year scopes are proved on PostgreSQL and in AC-TEN-48b.
- **D-P0-4, D-P1-1, D-P1-2, D-P0-5:** the immutability interceptor stays session-scoped; `OwnOrNational(Guid.Empty)` is an ordinary value; WorkflowPackSeeder reads national definitions only; agency default code overlays are stored as the kind's default template (no new column).
- **D-P4-1..6:** the ceiling lives in PLAN-47's validator (refused when added and at publish, rollback included); an agency's write to a national row is the guard's to refuse (403), registered before the tenant stamp (supersedes D-P0-3's placement); every validation call names the template's scope; agency A's acceptance custody v2 no longer holds `ACC-R-PRIV-WORKFLOW` (a stand-in for the national rule agencies could not see; AC-POL-47 publishes copies of it), while B and C keep the pre-ceiling layer for AC-TEN-48e.
- **Deviation (AC-TEN-48b):** positions are not created through the establishment flow on the stack (it needs a second approver in PLATFORM); previews in both tenants plus the PostgreSQL tests prove it.
- **Deviation (AC-TEN-48d):** the gateway sends an approver's `POST evaluate` to 403 ("missing role for this module"); `b-approver` reads the snapshot and `outsider-b` (agency B, hr-core) asks for the decision.
- **Deviation (P7 check 4):** two funded positions were not created on the demo (two approvals per change with users the server checks do not drive); proved on PostgreSQL and through the real change applier.
- **Deviation (P-9 prefixless scan after the run):** the script ran once on a freshly seeded prod-like stack (0); the retry could not start the stack because Windows reserved TCP 53248-53347, which covers ClamAV's host port 53310 (freeing it is a system change for the owner). AC-TEN-48a's own check (no issuance starting with '-') passed in the green run.
- **P-3 least-privilege class:** added (`AcTen48LeastPrivilegeTests`): the national read through the running services as `hr_core_app` under FORCE.
- **Deviation (the server's code after the merge):** the server runs 8abd5aa7fe93. The merge also carries 95228ffa (the CI fix), whose only server-side change is one HR.Core log line (the code-generation log no longer writes the scope key, for CodeQL). Not redeployed for a log line; the next deploy carries it.

## 4. Inventories

- **National tables and row security:** the ten tables of D-TEN-2; before: `tenant_isolation` only, ENABLE + FORCE; after: plus `national_shared_read` (SELECT, permissive) on exactly those ten, on both migration paths and on the server (bootstrap: "applied and verified (TEN-048)").
- **Readers and writers:** 20 bypasses replaced by `OwnOrNational`/`NationalOnly` (WorkflowStarter, WorkflowInstanceActionService, ExternalOutcomePublisher, WorkflowPanelBuilder, ExternalApprovalService, ApprovalStartCoordinator, WorkflowInboxQueryService, RecruitmentMetricsService, PolicyRuleValidationService, WorkflowDefinitionsController, ReferenceDataController, EstablishmentPayloadSummaryBuilder, UnifiedIntakeService, ReferenceDataProvider, ReferenceCatalogAdminService, PublishedPolicyImmutabilityInterceptor, WorkflowPackSeeder, WorkflowDefinitionHealthReport, MdmReferenceSeeder, the acceptance patches), plus the policy pages and API, the code generation pages and API, and `PolicyOrchestrator`. The architecture test forbids a general bypass on an `INationalShared` entity.
- **Code generation calls:** every caller of `ICodeGenerator` resolves overlay → national → none; no silent fallback in Production; the citizen reference is issued in the national tenant.
- **The ceiling's impact:** `reports/plan48-ceiling-impact.md` (acceptance: `ACC-R-PRIV-WORKFLOW` refused at publish and dropped at evaluation with the same workflow code from the national rule; the national rules now reach agencies; the server has no agency and no override, so nothing changes there).
- **Prefixless codes:** acceptance leftover before the plan: 4 (`-2026-00001`, PLATFORM and AGENCY-A); after: AC-TEN-48a and AC-13 assert none, and the scan of a fresh stack found 0. Server: 0 before and 0 after the deploy.

## 5. The guards and their own negative tests

| Guard | Negative test |
|-------|---------------|
| `NationalSharedWriteGuard` (EF) | `NationalSharedWriteGuardTests`; `Ten048CreateDraftScopeTests` (draft and rule of a national template from an agency → refused by the guard, not the stamp); AC-TEN-48f (403 `national-shared-write`, versions unchanged) |
| `national_shared_read` + `tenant_isolation` (row security) | `Ten048NationalRlsIntegrationTests` (6, `hrm_rls_app` NOSUPERUSER NOBYPASSRLS under FORCE: an agency's UPDATE/INSERT of a national row refused, another agency's rows invisible); `AcTen48LeastPrivilegeTests` |
| Check block of the policy | `NationalSharedArchitectureTests` (three lists equal, select-only), the SQL check (missing or extra policy fails), the server bootstrap |
| `MissingPolicy=Refuse` | `Ten048ResolutionTests`; AC-TEN-48c (422, no issuance, no counter) |
| `CodePolicyValidator` | `CodePolicyValidatorTests`; overlay packs refused whole |
| Counter never lowered | `Ten048CodePolicyUpgradeIntegrationTests` (two tenants, a second run changes nothing); the server check (no counter below the highest issued) |
| The ceiling | `PolicyCeilingTests`; `Ten048CreateDraftScopeTests`; `Ten048OverlayProvisioningTests`; AC-TEN-48e |
| `CreateDraft` scope | `Ten048CreateDraftScopeTests` (service, API, page); AC-TEN-48f |

## 6. Results

- **Unit:** full suite 5758/5758 on the final code (Windows); 5753/5753 in the Linux container of `ci-local.ps1`.
- **Integration (Testcontainers):** Ten048, CodeGeneration, Pol047, Policy, NationalSeed, CrossTenantRls, Hcm025, Prod001, S2s042, Wf04, Mig003, CodSequence: 131/131 at P4, 23/23 after the review fixes.
- **Acceptance (local, P-10):** prod-like run 2: journey 1, main 15 (AC-HCM-25 a..h, AC-WF-43, AC-TEN-48 a..g), AC-INT-37 8, AC-MSG-40 8, AC-WF-44 5, AC-WF-45 8, AC-WF-46 9, AC-POL-47 1, AC-S2S-42 9, AC-WF-46 orphans 1; least-privilege 9 (AC-OPS-38 8, AcTen48LeastPrivilegeTests 1); lab main 39 (AC-13 with 120 issuances in two tenants and exact counters, AC-10, AC-11, AC-32, AC-33, AC-EST-10..18, AC-EST-24, AC-HCM-21, AC-HCM-30, AC-WF-23) and the lab journey 1.
- **CI:** PR run 42767277527 on 95228ffa was 14 of 14 green, after one fix round. The red run 42729557053 on ccf95c6b had three causes:
  - the acceptance data verification still expected 64 auth users, and the new persona makes 65;
  - a log line CodeQL reports as log forging;
  - a ClamAV host-port collision.

  In the green run, the PLAN-48 classes all passed:
  - AC-TEN-48 a..g in prodlike main, 15/15;
  - `AcTen48LeastPrivilegeTests` in leastpriv, 9/9;
  - AC-13 in lab main (required; guard PASS);
  - AcQa41, 4/4.

  The four payroll classes marked not required (Ac21, Ac23, Ac24, Ac25) fail exactly as they do on main.
- **Evidence of record:** the PR run's acceptance-evidence artifact, https://github.com/omardefdev/HRManagement/actions/runs/42767277527/artifacts/13741829144 (details in the merge note of `reports/plan48-progress.md`). Local evidence was kept under `artifacts/plan48/` and never committed.

## 7. The demo server

- **Before** (read-only, P0 and P7): tag 61816ed7dda2; PLATFORM only; 20 national policy templates, no override; counters ArchiveReference LEAVE 12, AssetTag 12, CitizenReference 2, IdCardSerial 10, RecruitmentRequisition `…|2026|2026` 15, ServiceTransaction LEAV 12 and SEP 1 (each equal to its highest issued number); 500 positions `FP-0001..0500`; 0 prefixless; `SeedPackApplications` = NATIONAL-MANDATORY-v1.
- **Changes and undo:** backup `/opt/hrm/backups/pre-plan48-20261109T194711Z` (verified; delete when no longer needed); deploy 8abd5aa7fe93 (undo: raise the shared AssetTag counter with the SQL in `OPS_VPS_SINGLE_HOST.md`, then `deploy.ps1 -Rollback`; roll forward is preferred); the read policy (harmless to the previous tag); the code policy upgrade (no undo: the previous tag reads v2 with the same keys).
- **After:** the policy on exactly the ten tables; the upgrade recorded once, v1 archived, v2 published; every counter unchanged and none below its highest issued number; the new requisition key raised to 15 and then 17 (REQ-2026-00016 and -00017 issued by the PLAN-43/44 server tours); 621 numbers outside the national formats reported, none guessed; 0 prefixless; `/CodePolicies` shows the national templates; smoke 50/0/1 (the known client-side port probe); AcOps36 (36j excepted), AcInt37 and the PLAN-38..47 server checks green (the AC-WF-46 sweep check after idcard-web's first cycle), and the new `AC_TEN_48_vps_…` check.
- **Rollback note (P-5):** the previous release shares one AssetTag counter between AH and VIP again; raise it to the VIP counter before a rollback (SQL in the runbook), or roll forward.

## 8. What OPS-D must take (P-4)

Production connects HR.Core as `hr_core_app` under forced row security, so every tenant-less read below sees nothing there (the
national rows excepted, through `national_shared_read`): `LeaveScheduleReconcilerJob` (a no-op everywhere), `RecruitmentIntakeCloseJob`,
`RecruitmentOfferExpiryJob` (the expiry read), `RecruitmentReserveExpiryJob`, `CandidateNotificationJob`, `CandidateRetentionJob`
(its tenant list comes from tenant tables), `FileReferenceOutboxDispatcherJob`, `InboxRetentionHostedService`, the start-up passes
after the seeds (`WorkflowDefinitionHealthReport`, `OrgUnitPathService.RecomputeAllAsync`, `OrgUnitIntegrationService` reads),
HR.Contracts `HireCommencementWatcher`, File.Storage `FileScanProcessorJob`, every anonymous `/careers` path (postings, apply, track,
competitions, results, the OTP lookups), and agency provisioning (it writes another tenant's rows from the operator's session).
The pattern to follow is `WorkflowOutcomeReconciliationJob` and `CodeCounterReseeder`: tenants from `Tenants`, `TenantIdFlow` per
tenant, a new scope (and connection) per tenant.

## 9. Deferred items

- A unique index on `PolicyTemplates (TenantId, Domain, Scope)` (two concurrent first drafts of one agency domain can create two
  override templates; an EF migration over existing data).
- A start that fails its reseed for one tenant stops (by design; `Seed:AllowMandatoryFailure` is the escape) — an operator runbook
  step if a tenant's numbers cannot be read.
- Agencies provisioned before PLAN-48 with the keyed `FIN-ORG-UNIT` overlay take the national OrgUnit format for keyless requests.
- An agency's own `Scope = National` template (made by the old `CreateDraft`) no longer applies (none on the server or in the seed).
- Two funded positions on the demo through the establishment flow (a server check driving two approvers).

## 10. Defects for later plans

- `GET api/codes/policies` and `GET api/policies/templates/{id}` serialise EF graphs with back-navigations: a JSON cycle and a 500
  (present at the base 61816ed7; no test calls them).
- The acceptance compose maps ClamAV to host port 53310. That port is inside Linux's ephemeral range and inside a range Windows may
  reserve dynamically (53248-53347 here). Locally, the stack cannot start until the reservation moves. On GitHub runners the bind
  collides now and then: three of about 49 stack starts (PLAN-48's first run, and the nightly 42593445588 on main). Suggested fix:
  `"6350:3310"`, or drop the unused host mapping. It is proposed as a separate task.

## 11. Timing

- **Wall-clock:** 15 h 41 min in all.
  - Started 2026-11-09T15:27:17Z (progress file).
  - Last phase commit before the PR 2026-11-09T23:15Z.
  - First push 2026-11-10T00:36:00Z.
  - Merged 07:08:47Z.
- **Active time against waiting for CI:** 9 h 09 min of work before the first push. From the first push to the merge took 6 h 33 min:
  - CI ran 1 h 28 min: 36 min 33 s red, then 51 min 02 s green;
  - 3 h 07 min idle until the owner's Auto-fix instruction;
  - about 45 min for the fix and the local gates;
  - the push then waited for a read-only CI-risk review until the owner said to continue.
- **Phases:** P0 35 min, P1 24 min, P2 66 min, P3 17 min, P4 44 min, P5 3 h 49 min (two prod-like runs, one with a cold build, a lab
  run, least-privilege reruns, the review and the verification workflow), P6/P7 58 min.
- **Builds, acceptance and deploy:** local prod-like run 1 44 min (stack 1145 s, cold), run 2 56 min (with the image build), lab
  19 min, least-privilege stage 6 min; server build 1244 s plus 16 s push; deploy 128 s to all healthy; server suites 19 min 47 s.
- **Actions minutes:**
  - run 1: 146.6 runner-minutes (153 billed, per job rounded up);
  - run 2: 221.9 runner-minutes (230 billed);
  - the post-merge run: under 1 min (deduplicated).
- **`scripts/plan-timing.ps1 -From 25 -To 48`** (after the merge; Riyadh time):
  - PLAN-48: 11 commits, first commit 2026-11-09 18:28, end 2026-11-10 02:18, span 7:50, wall-clock 7:51. The script stops at the
    feature merge into release/plan-48, before the PR wait.
  - For comparison: PLAN-47 24:24, PLAN-46 15:32, PLAN-45 6:15, PLAN-44 19:14.

## 12. Readiness note for PLAN-49

PLAN-49 builds on:

- `OwnOrNational`, `NationalOnly`, `OwnFirst` (ServiceDefaults) and `NationalReadQueries` (HR.Core): the only way to read a national
  table; `national_shared_read` under forced row security.
- The ceiling: `PolicyCeiling` (SharedKernel), `PolicyRuleDefinition.Origin`, the validator's `IsAgencyOverride`/`NationalSetFields`/
  `NationalTriggersWorkflow`, and `IPolicyRuleValidationService` taking the template's scope on every call.
- PLAN-47's validator now sees the national schema's fields from an agency; retired triggers (R-HIRE-START, R-SEP-START, R-TKT, R-TRF)
  and R-EST-SUBMIT's missing binding remain for PLAN-49 (the start-up inventory lists them).
- Agency seeding through the publish service (`AgencyPolicySeeder`), and `PolicyDraftService.CreateDomainDraftAsync`.

Files it shares with this plan: `PolicyOrchestrator.cs`, `PolicyEvaluator.cs`, `PolicyRuleValidator.cs`, `PolicyRuleValidationService.cs`,
`PolicyDraftService.cs`, `PolicyPublishService.cs`, `PolicySeedChangeSets.cs`, `WorkflowPackSeeder.cs`, `PolicyEngineSeeder.cs`,
`AcceptanceDataSeeder.cs` (custody v2), and the gateway's policy rules (`GatewayPathAuthorizationMiddleware.cs`).
