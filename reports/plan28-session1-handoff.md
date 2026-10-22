# PLAN-28 — Session 1 handoff (Step 0, A, B, C, D, E)

**Date:** 2026-10-22
**Branch:** `feature/plan-28-committee-appeal-governance` (local only — not pushed), created from the tip of `feature/plan-27-offer-merit-reserve` @ `d8f6714` (nothing is merged to `main`).

## 1. Commits

| SHA | Message |
|-----|---------|
| `74de93c` | feat(hcm028): committee model — purpose, chair, per-candidate COI, votes, decisions, requisition switch |
| `e1ae8c6` | feat(hcm028): committee submit/lock rules and server-side approval checks |
| `6a79f1c` | feat(hcm028): ranking gate on committee approval + bulk apply |
| `e794ac1` | feat(hcm028): committee minutes PDF + UI |
| `68c73f9` | chore(hcm028): acceptance seed for committee-gated requisition |
| `a2ed412` | chore(hcm028): seed the committee requisition in the RIF branch (br1-mgr scope) |
| *(this commit)* | docs(hcm028): session 1 handoff |

The baseline (`reports/plan28-baseline.md`) is part of `74de93c`. Run `git log --oneline d8f6714..HEAD` for the final SHA.

## 2. Migrations

Both generated after `20261021204340_HCM027_ReserveList`. New columns and tables only — **no data step, no existing values rewritten.**
`dotnet ef migrations has-pending-model-changes` → **No changes**.

### `20261022070018_HCM028_CommitteeDecisions` (commit 1)

| Table | Change |
|-------|--------|
| `SelectionCommitteeSessions` | + `Purpose` int not null, **default 1 (Ranking)** — hand-edited from EF's 0 so existing sessions get the same default as new ones; + `SubmittedAt`, `LockedAt` (timestamptz null); + `ChairEmployeeId` (uuid null, no FK) |
| `RecruitmentRequisitions` | + `RequiresCommitteeApproval` boolean not null default false |
| `SelectionCommitteeMemberCois` (new) | `MemberId` → members **cascade**, `ApplicationId` → applications **cascade**, `HasConflict`, `DeclaredAt`, `RecordedBy`; unique `(MemberId, ApplicationId)` |
| `SelectionCommitteeVotes` (new) | `SessionId` → sessions **cascade**, `MemberId` → members **cascade**, `ApplicationId` → applications **cascade**, `Vote` int, `Comment` varchar(1000), `RecordedBy`, `RecordedAt`; unique `(MemberId, ApplicationId)`; index `(TenantId, SessionId)` |
| `SelectionCommitteeDecisions` (new) | `SessionId` → sessions **cascade**, `ApplicationId` → applications **cascade**, `Recommendation` int, `ProposedRank` int null, `Rationale` varchar(2000); unique `(SessionId, ApplicationId)` |

**Duplicate members (A3):** no new index and no pre-check. `IX_SelectionCommitteeMembers_TenantId_SessionId_EmployeeId` has been **unique** since the committee tables were created, so duplicates cannot exist (a duplicate add used to throw a `DbUpdateException`; the service now refuses it with a message). The migration says so in a comment. `scripts/hcm028-find-duplicate-committee-members.ps1` is the read-only sanity check (exit 0 none / 2 found), modelled on the HCM-026 script.

### `20261022071057_HCM028_RankOverride` (commit 3)

`RecruitmentApplications` + `RankOverrideReason` varchar(1000) null, `RankOverriddenBy` uuid null, `RankOverriddenAt` timestamptz null.

RLS: new tenant tables follow the HRCore convention (no `EnableRls` in the migration; `scripts/run-migrations.sh` → `ten002c-harden-tenant-rls.sql` covers every table with `TenantId`).

## 3. New and changed types

### Enums (all new, stored as **int** like `RecruitmentApplicationStatus`/interview enums; `Display` names in Arabic)

| Enum | Path | Members |
|------|------|---------|
| `CommitteePurpose` | `HR.Core.Domain/Enums/CommitteePurpose.cs` | Interview=0 «مقابلة», Ranking=1 «ترتيب المرشحين», Final=2 «قرار نهائي» |
| `CommitteeVoteValue` | `HR.Core.Domain/Enums/CommitteeVoteValue.cs` | Agree=0, Disagree=1, Abstain=2 |
| `CommitteeRecommendation` | `HR.Core.Domain/Enums/CommitteeRecommendation.cs` | Recommend=0, Reserve=1, Reject=2 |

### Domain (`src/Services/HRCore/HR.Core.Domain`)

| Type | Path | Notes |
|------|------|-------|
| `SelectionCommitteeSession` (changed) | `Entities/SelectionCommitteeSession.cs` | + `Purpose` (default **Ranking**), `SubmittedAt`, `LockedAt`, `ChairEmployeeId`, `[NotMapped] IsLocked`, navs `Votes`, `Decisions` |
| `SelectionCommitteeMember` (changed) | `Entities/SelectionCommitteeMember.cs` | consts `ChairRole = "Chair"`, `MemberRole = "Member"`; `Vote` marked `// legacy — PLAN-28` (not read/written); nav `CoiDeclarations` |
| `SelectionCommitteeMemberCoi`, `SelectionCommitteeVote`, `SelectionCommitteeDecision` (new) | `Entities/*.cs` | `AuditableEntity`, tenant-filtered by the global filter |
| `RecruitmentRequisition` (changed) | `Entities/RecruitmentRequisition.cs` | + `RequiresCommitteeApproval` |
| `RecruitmentApplication` (changed) | `Entities/RecruitmentApplication.cs` | + `RankOverrideReason`, `RankOverriddenBy`, `RankOverriddenAt` |
| `SelectionCommitteeRules` (new) | `Services/SelectionCommitteeRules.cs` | see §5; also `CommitteeVoteTally` record |

### Infrastructure (`src/Services/HRCore/HR.Core.Infrastructure`)

| Type | Path | Notes |
|------|------|-------|
| `SelectionCommitteeService` (rewritten, ctor unchanged `(HRCoreDbContext)`) | `Services/Recruitment/SelectionCommitteeService.cs` | §5 |
| `SelectionCommitteeWorkspace` (new record) | `Services/Recruitment/SelectionCommitteeWorkspace.cs` | `SubmitIssues`, `SuggestedRanks`, `TallyFor`, `HasConflict`, `VoteOf`, `DecisionFor` |
| `SelectionCommitteeLifecycleService` (changed) | `Services/Recruitment/SelectionCommitteeLifecycleService.cs` | **ctor gained** `SelectionCommitteeService committeeService, IWorkflowInstanceActionService workflowActions` (last two) |
| `SelectionCommitteeWorkflowHandler` (changed, ctor unchanged) | `.../SelectionCommitteeWorkflowHandler.cs` | approved → `ApprovedAt`; rejected → `WorkflowInstanceId`/`SubmittedAt`/`LockedAt` = null + audit `CommitteeWorkflowRejected` (UserId = last history actor) |
| `SelectionCommitteeMinutesService` (new) + records `SelectionCommitteeMinutesData/Member/Candidate/Approval` | `.../SelectionCommitteeMinutesService.cs` | `GetMinutesAsync(sessionId)`; registered in `Program.cs` |
| `RecruitmentSelectionService` (changed, ctor unchanged `(HRCoreDbContext)`) | `.../RecruitmentSelectionService.cs` | §5 |
| `CommitteeRankingGate` (new record) | `.../CommitteeRankingGate.cs` | `(bool Required, SelectionCommitteeSession? ApprovedSession)`, `DecisionFor(appId)` |
| `WorkflowInstanceActionService` (changed) | `Services/Workflow/WorkflowInstanceActionService.cs:167` | `ValidateSelfApprovalAsync`: creator check for `SelectionCommitteeSession` (all steps). Nothing else changed |
| `RecruitmentOptions` (changed) | `.../RecruitmentOptions.cs` | §4 |
| `HRCoreDbContext` (changed) | `Data/HRCoreDbContext.cs` | 3 DbSets + mappings (§2) |
| `AcceptanceDataSeeder` (changed) | `Seeding/AcceptanceDataSeeder.cs` | `SeedCommitteeGatedRequisitionAsync` (§6) |

### Shared

`src/Shared/HRManagement.ServiceDefaults/AcceptanceSeedIds.cs` + `CommitteeRequisitionId`, `CommitteeSessionId`, `CommitteeRequisitionNumber`. **No change in `HRManagement.MessageContracts`.**

### Web (`src/Services/HRCore/HR.Core.Web`)

| Item | Notes |
|------|-------|
| `SelectionCommitteesController` | ctor: `IWorkflowInstanceActionService` removed, `SelectionCommitteeMinutesService` added. Actions: `CreateSession(+purpose)`, `AddMember(sessionId, employeeId, role, hasConflictOfInterest)` (the unused `requisitionId` param dropped), `RemoveMember`, `SetChair`, `RecordCoi`, `RecordVote`, `UpsertDecision` (a Reject drops the posted rank), `Submit`, `ApproveWorkflow`/`RejectWorkflow` → lifecycle (org-scope check kept), `GET Minutes/{id}` (HrCoreReadOnly; `SecurityAudit` BulkExport) |
| `RecruitmentRequisitionsController` | ctor + `RecruitmentSelectionService selection, IOptions<RecruitmentOptions>`; Create/Edit carry `RequiresCommitteeApproval` (Edit refuses a change after the first rank); `Details` fills `LatestCommitteeSession`, `CommitteeGate`, `RankingStarted`; new POST `ApplyCommitteeRanking(id)`, `SetCommitteeRequirement(id, required)` (both `HrCoreWrite`) |
| `RecruitmentApplicationsController` (ctor unchanged) | `Details` fills `CommitteeGate`; `AssignRank` passes `RankOverrideReason` + actor |
| `SelectionCommitteeWorkflowUiService` (ctor unchanged), `WorkflowInboxEligibilityService` | committee block reason = `SelectionCommitteeRules.ValidateApproval(...)`; creator message = the committee message; `BuildOptions(instance, context, Guid? actorEmployeeId = null)` |
| `SelectionCommitteeMinutesDocument` (new) | `Services/SelectionCommitteeMinutesDocument.cs` — `Generate(SelectionCommitteeMinutesData) → byte[]` (A4 landscape, `ConfigureOfficialPage`) |
| View models | `AssignRankForm.RankOverrideReason`; `SelectionCommitteeDetailsViewModel.Workspace`; `RecruitmentApplicationDetailsViewModel.CommitteeGate/CommitteeDecision`; `RecruitmentRequisitionCreateViewModel.RequiresCommitteeApproval`; `RecruitmentRequisitionDetailsViewModel.LatestCommitteeSession/CommitteeGate/RankingStarted` |
| Views | `SelectionCommittees/Details.cshtml` (rewritten: lock banner + «تحميل المحضر», members with employee picker `data-hr-picker="employee"`, vote matrix with per-candidate COI, decisions with suggested-rank pre-fill, submit panel listing unmet requirements, workflow panel); `SelectionCommittees/Index.cshtml` (purpose, names; the raw-GUID add-member form removed — members are managed on Details); `RecruitmentRequisitions/{Create,Edit}.cshtml` (checkbox), `Details.cshtml` + new `_CommitteePanel.cshtml`; `RecruitmentApplications/Details.cshtml` (committee decision, proposed-rank pre-fill, override reason input, non-Assessed message). No inline handlers/styles; confirms use `data-hr-confirm`. |

## 4. `RecruitmentOptions` and appsettings

```csharp
/// Recruitment:CommitteeApprovalDefault — initial RequiresCommitteeApproval on new requisitions
public bool CommitteeApprovalDefault { get; set; }   // default false
```

`appsettings.Production.json` and `appsettings.Staging.json`: `"Recruitment": { "CommitteeApprovalDefault": true }`. Development/Acceptance (`ASPNETCORE_ENVIRONMENT=Acceptance`, no override file) keep `false`, so requisitions created by the existing acceptance tests are not gated.

## 5. Public signatures for session 2

```csharp
// ── Membership (appeal reviewer independence) — SelectionCommitteeService(HRCoreDbContext db)
Task<IReadOnlyList<Guid>> GetMemberEmployeeIdsAsync(Guid requisitionId, CancellationToken ct = default)
    // employee ids on ANY session of the requisition, recused members included
Task<bool> IsCommitteeMemberAsync(Guid requisitionId, Guid employeeId, CancellationToken ct = default)

// ── Ranking (appeal rank correction) — RecruitmentSelectionService(HRCoreDbContext db)
Task<(bool Success, string? Error)> AssignRankAsync(
    Guid applicationId, int rank, string? rankOverrideReason = null, Guid? actorUserId = null, CancellationToken ct = default)
    // order: rank >= 1 → status Assessed|Ranked («يمكن الترتيب للطلبات المُقيَّمة فقط.») → no pending appeal
    //        → requisition link → gate (if RequiresCommitteeApproval) → duplicate rank
    // gate: latest approved session with Purpose Ranking|Final; decision Recommend|Reserve;
    //       rank == ProposedRank, or reason >= 20 chars (stored + EntityAuditLog "RankOverride");
    //       matching the proposed rank clears earlier override fields. Gate off: reason ignored.
Task<(bool Success, string? Error, int RankedCount)> ApplyCommitteeRankingAsync(Guid requisitionId, Guid? actorUserId, CancellationToken ct = default)
Task<CommitteeRankingGate> GetCommitteeRankingGateAsync(Guid requisitionId, CancellationToken ct = default)
Task<bool> HasRankingStartedAsync(Guid requisitionId, CancellationToken ct = default)
Task<(bool Success, string? Error)> SetCommitteeApprovalRequiredAsync(Guid requisitionId, bool required, Guid? actorUserId = null, CancellationToken ct = default)

// ── Lifecycle — SelectionCommitteeLifecycleService(db, policyOrchestrator, workflowStarter, configuration,
//                                                  SelectionCommitteeService committeeService, IWorkflowInstanceActionService workflowActions)
Task<(bool Success, string? Error)> SubmitForApprovalAsync(Guid sessionId, Guid? actorUserId, string? actorUserName, CancellationToken ct = default)
Task<(bool Success, string? Error)> ApproveStepAsync(Guid sessionId, WorkflowActionActor actor, Guid? actorEmployeeId, string? comments, CancellationToken ct = default)
    // passes targetStepLevel = current step to ApproveAsync
Task<(bool Success, string? Error)> RejectStepAsync(Guid sessionId, WorkflowActionActor actor, Guid? actorEmployeeId, string? comments, CancellationToken ct = default)
const string NoWorkflowMessage = "لا يوجد مسار اعتماد مرتبط بهذه الجلسة."

// ── Committee data — SelectionCommitteeService
Task<(SelectionCommitteeSession?, string?)> CreateSessionAsync(Guid requisitionId, DateTime committeeDate, string? findings, string? recommendation,
    CommitteePurpose purpose = CommitteePurpose.Ranking, CancellationToken ct = default)
Task<(bool, string?)> AddMemberAsync(Guid sessionId, Guid employeeId, string? role, bool hasConflictOfInterest, Guid? actorUserId = null, CancellationToken ct = default)
Task<(bool, string?)> RemoveMemberAsync(Guid sessionId, Guid memberId, Guid? actorUserId = null, CancellationToken ct = default)
Task<(bool, string?)> SetChairAsync(Guid sessionId, Guid memberId, Guid? actorUserId = null, CancellationToken ct = default)
Task<(bool, string?)> RecordMemberCoiAsync(Guid sessionId, Guid memberId, Guid applicationId, bool hasConflict, Guid? actorUserId = null, CancellationToken ct = default)
Task<(bool, string?)> RecordVoteAsync(Guid sessionId, Guid memberId, Guid applicationId, CommitteeVoteValue vote, string? comment, Guid? actorUserId = null, CancellationToken ct = default)
Task<(bool, string?)> UpsertDecisionAsync(Guid sessionId, Guid applicationId, CommitteeRecommendation recommendation, int? proposedRank, string? rationale, Guid? actorUserId = null, CancellationToken ct = default)
Task<SelectionCommitteeWorkspace?> GetWorkspaceAsync(Guid sessionId, CancellationToken ct = default)
Task<IReadOnlyList<RecruitmentApplication>> GetCandidatesAsync(Guid requisitionId, CancellationToken ct = default)   // Assessed|Ranked, suggested order

// ── SelectionCommitteeRules (pure)
MinimumQuorum = 3, QuorumStepLevel = 2, FinalApprovalStepLevel = 3, MinRankOverrideReasonLength = 20
CandidateStatuses {Assessed, Ranked}; RankingPurposes {Ranking, Final}; IsChairRole(role) ("Chair" or legacy "رئيس")
EligibleVoters, ValidateVoter, Tally → CommitteeVoteTally(EligibleVoters, Agree, Disagree, Abstain), IsSupported (Agree*2 > eligible)
GetSubmitIssues / ValidateForSubmit(members, coi, votes, decisions, candidates)   // note: + coi parameter vs. the prompt
SuggestRanking / SuggestedRanks (score desc, nulls last, AppliedAt asc, Id)
ValidateApproval(currentStepLevel, members?, sessionCreatedBy, actorUserId, actorEmployeeId)
ValidateRankAgainstDecision(decision, rank, reason)
Messages: LockedMessage, QuorumMessage, ApproverIsMemberOrCreatorMessage, NotAssessedMessage, GateNoApprovedSessionMessage,
          GateRejectedMessage, GateNoDecisionMessage, RankOverrideReasonMessage, RequirementFrozenMessage, RecusedVoterMessage, ConflictedVoterMessage
```

For appeal rank correction (H3, `UpheldRankCorrection` → `Assessed` → `AssignRankAsync(correctedRank, rankOverrideReason: rationale)`): the rationale must be ≥ 20 chars **only** when the requisition is gated and the corrected rank differs from the committee's proposed rank (H3 already requires ≥ 30). On a gated requisition, a candidate the committee **rejected** (`GateRejectedMessage`) or never decided on (`GateNoDecisionMessage`) cannot be ranked even with a reason — session 2 must decide whether an appeal may override that (today it cannot) and either relax the gate for appeal corrections or limit the outcome.

## 6. Seeded data (Agency A, `AcceptanceDataSeeder.SeedCommitteeGatedRequisitionAsync`)

`testdata/acceptance/recruitment-states.json` does not exist, so the ids are listed here (all deterministic).

| Item | Value |
|------|-------|
| Requisition | `a0000001-0000-4000-8000-000000000510` — `REQ-2026-A-CMT1`, Published, `RequiresCommitteeApproval = true`, Law 50/2004, closing date ReferenceDate + 2 years, public posting `720d6173-2e51-674f-ab9e-d03c41df518e` |
| Position | `a106924f-8f34-6848-aadd-a571084f2d36` — `VAC-0016`, grade G3, org unit `4898ee06-0382-324f-90b0-ad19c81d69ca` «مديرية تقانة المعلومات», site **RIF** (inside `br1-mgr` scope `a0000001-…-0102`) |
| Criteria | `RecruitmentSeedCriteria` (interview 40 % / experience 60 %), active, locked by the seeded scores |
| Candidates (Assessed, interview Attended + one assessor) | `59f7f5cb-f384-8c4c-a8aa-8137f8e324c3` هبة المصري 88 · `50fc0b13-d45e-b649-a2dc-e8ac3b45b00c` طارق الحلبي 76 · `d74fa8ec-c701-394f-b837-fa80a487896d` ريم الشامي 64 (suggested ranks 1/2/3) |
| Session | `a0000001-0000-4000-8000-000000000511` — Purpose Ranking, **unsubmitted**, no votes, no decisions, `CreatedBy` null |
| Members (member id → employee) | Chair `c48a4eaa-db40-1843-a9b3-d444a40e7bf9` → `e4b65b57-584f-d842-8c6b-4324441cad16` A-00027 نادية لمى البيطار · `d2552c0d-ab66-5d41-a171-c91aab624549` → `4c2fe080-6243-d24b-93a4-4cce36fbbd68` A-00028 جمال خضر المصري · `98134ffa-8108-0345-852e-0874bc89b0ad` → `38fe721e-a767-3e4a-a448-cae5ef8c3455` A-00029 ياسمين الزعبي · `4cdda0fc-dca9-b344-9030-9a2f954df068` → `ec716ac1-3e93-4245-9c65-f0c67c8c1f4c` A-00030 ماهر طلال حداد (all HQ, Active) |

Members are active employees **not linked to any persona** (stricter than "not a-management-approver/br1-mgr/central-a"), so no approver or compliance persona is a member. For the "member approves step 3" negative, add a persona's employee as a member at runtime (before submit). `CreatedBy` is null because the seed has no user; for the "creator approves step 3" negative create a new session through the UI (the audit interceptor stamps `CreatedBy`). The seed fingerprint is unchanged, so **existing acceptance databases do not get this data** — run `scripts/acceptance-reset.ps1` first.

Intended flow: `central-a` records votes/decisions (or edits them) → submits → `br1-mgr` approves steps 1–2 → `a-management-approver` step 3 → «تطبيق ترتيب اللجنة» on the requisition (or per-application ranking).

## 7. Test results

| Run | Baseline | Now |
|-----|----------|-----|
| Prompt filter (`Hcm02\|Recruitment\|Offer\|Selection\|Committee\|Appeal\|Fun001\|Syr001`) | 281 passed / 2 failed | covered below |
| Wide regression (prompt filter + `Wf02\|WorkflowInbox\|Est024\|Reserve\|Acceptance`) | — | **379 passed / 2 failed** (same 2) |
| `Hcm028*` | — | **48 passed** |
| Full unit suite | PLAN-27: 2860 passed / 8 failed | **2947 passed / 8 failed** — the same 8 pre-existing failures |

Pre-existing failures (unchanged): `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider`, `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50`, `BolaArchitectureTests.Employee_scoped_controller_actions_apply_org_scope_guard` (HRContracts/Payroll offenders only), `Ten002ArchitecturalTests.IgnoreQueryFilters_only_in_sanctioned_bypass` (`BenefitPlanSeeder.cs`), `Prv001RopaTests…`, `San001SanctionsTests.Validate_script_passes`, `Str005ProductionBindingTests` ×2.

Build: `dotnet build HRManagement.slnx` → 0 errors, 10 pre-existing warnings. Integration and acceptance tests compiled, **not run** (no local stack). UI verified by compilation (Razor views compile at build) and service-level tests only — not exercised in a browser.

New test classes: `Hcm028CommitteeRulesTests` (18 incl. theory cases), `Hcm028CommitteeServiceTests` (11), `Hcm028CommitteeApprovalTests` (6), `Hcm028RankingGateTests` (11), `Hcm028CommitteeMinutesTests` (2), `Hcm028TestSupport` (fixtures); `AcceptanceSeedTests.Acceptance_seed_committee_gated_requisition_is_ready_for_votes` (new test in an existing class).

Modified existing tests:
- `Hcm022RecruitmentWorkflowsUiTests.CommitteeSubmit_rejects_without_members` — new `SelectionCommitteeLifecycleService` ctor arguments (`SelectionCommitteeService`, `IWorkflowInstanceActionService` mock); assertion unchanged.

No existing test relied on ranking a non-Assessed application (`Hcm026InterviewLifecycleTests` ranks an Assessed one), so no setup changed for C. `OfferEligibilityRules`/`GetBestAvailableRankAsync` are untouched and their tests pass.

## 8. Deviations, known issues, markers

Deviations:
- **Creator blocked at every committee step, not only step 3.** The panel and inbox already blocked the creator at all steps (`EntityCreatedBy`); enforcing it server-side at step 3 only would let the UI and server disagree. Members are blocked at step 3 only; quorum is checked at steps ≥ 2. The creator message is now «لا يجوز لعضو في اللجنة أو منشئها اعتماد الجلسة.» everywhere for committees.
- **No duplicate-member migration pre-check** (A3): the unique index already existed (see §2).
- `ValidateForSubmit` takes the per-candidate COI list as an extra parameter (eligible voters need it). `GetSubmitIssues` returns all issues for the UI; the server returns the first.
- Chair: a chair must also be non-recused; `AddMember` with role Chair and a conflict is added as a plain (recused) member. Legacy free-text role «رئيس» counts as chair.
- `Purpose` default is **Ranking** (entity and migration), so the gate works with sessions created without choosing a purpose.
- Recording a per-candidate conflict deletes that member's vote on that candidate (it would not be counted anyway).
- `UpsertDecision` (controller) drops the posted rank when the recommendation is Reject; the service still refuses a Reject with a rank from other callers.
- `ApplyCommitteeRankingAsync` works whenever an approved Ranking/Final session exists, even if the requisition gate is off; it returns a third tuple item (count). It clears the target applications' ranks first (two saves in one transaction) so swapped ranks never violate the unique `(TenantId, RequisitionId, Rank)` index; an outsider holding a proposed rank aborts the run.
- `RejectStepAsync` has no SoD check (rejection only returns the session to rework). The stage guard and `ValidateSelfApprovalAsync` apply to approvals only; a rejection goes straight to `IWorkflowInstanceActionService.RejectAsync`.
- The member check needs the `employee_id` claim; an actor without it is only subject to the creator rule.
- Minutes PDF calls `HrQuestPdfBootstrap.EnsureInitialized()` itself (QuestPDF checks the license before the page lambda runs). Minutes are generated on demand from locked data; no file is stored.
- The seeded requisition sits in the RIF branch (commit `a2ed412`), because session 2 drives steps 1–2 with `br1-mgr`.

Known issues / limits:
- **Policy Off + not lab scale:** submit locks the session but starts no workflow (existing branch kept, `// PLAN-25` marker), so the session can be neither approved nor unlocked through the UI. Same shape as appeal H5 — session 2 may want to start the workflow there too.
- `SelectionCommitteeMember.HasConflictOfInterest` (session-wide) is still what `RecruitmentScoringService` checks for assessors; per-candidate conflicts are not consulted there (unchanged behaviour).
- Existing acceptance databases are not re-seeded (fingerprint unchanged).
- UI not exercised in a browser; acceptance/integration not run.
- Local-only: `global.json` (`rollForward`) left uncommitted.

Markers:
- `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/SelectionCommitteeLifecycleService.cs:73` — `// PLAN-25: always start workflow`
- `src/Services/HRCore/HR.Core.Web/Controllers/SelectionCommitteesController.cs:163` — `// PLAN-29: archive minutes via File.Storage`
- `src/Services/HRCore/HR.Core.Domain/Entities/SelectionCommitteeMember.cs:32` — `// legacy — PLAN-28` (`Vote` column)
- No PLAN-30 markers were needed in session 1 (no candidate-facing committee events).

## 9. Notes for session 2

- Continue on `feature/plan-28-committee-appeal-governance`; new migrations go after `20261022071057_HCM028_RankOverride`.
- Reviewer independence: `SelectionCommitteeService.GetMemberEmployeeIdsAsync(requisitionId)` covers every session of the requisition (recused members included). Interview panel members: `RecruitmentInterviewPanelMembers` by slot; assessors: `RecruitmentAssessmentScores.AssessorEmployeeId`.
- Rank correction: see the gate note at the end of §5. If an upheld appeal must be able to rank a candidate the committee rejected, add an explicit appeal path (e.g. an internal `AssignRankAsync` overload that skips the decision check but keeps the audit) rather than weakening the public gate.
- New `new SelectionCommitteeLifecycleService(...)` call sites need the two new ctor arguments; `Hcm028TestSupport.CreateLifecycle(db)` builds a real one (lab scale, policy Off, real starter and action service).
- Acceptance: use the seeded ids in §6. `ApproveWorkflow`/`RejectWorkflow` keep their routes and form field names (`SessionId`, `Comments`); `AddMember` still accepts `sessionId`, `employeeId`, `role`, `hasConflictOfInterest`. New committee forms post `sessionId`, `memberId`, `applicationId`, `vote` (`Agree|Disagree|Abstain`), `hasConflict`, `recommendation` (`Recommend|Reserve|Reject`), `proposedRank`, `rationale`. Ranking: `RecruitmentRequisitions/ApplyCommitteeRanking/{id}`, or `RecruitmentApplications/AssignRank` with `ApplicationId`, `Rank`, `RankOverrideReason`. TempData is not observable through the gateway — assert DB state (`SelectionCommitteeSessions."LockedAt"/"ApprovedAt"`, `RecruitmentApplications."Rank"/"RankOverrideReason"`, `AuditLogs."Action"` = `RankOverride`, `ApplyCommitteeRanking`, `CommitteeSubmitted`, `CommitteeWorkflowRejected`).
- `AcHcm22` committee scenario (22c) may need updating: the old flow submitted a session with members only; submit now also requires a chair and a majority-supported decision for every Assessed/Ranked candidate, and the quorum/creator/member checks are server-side.
- Docs to write: committee model and gate in `HCM_001_RECRUITMENT.md` (G-10), SoD rows (creator never; member not at step 3), RBAC rows (secretary = HR records votes on behalf of members), the `Recruitment:CommitteeApprovalDefault` option, and the minutes endpoint.
