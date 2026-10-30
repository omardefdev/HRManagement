using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-25a–h (PLAN-25): recruitment approvals with the Production settings (run on the prod-like profile,
/// <c>docker-compose.acceptance-prodlike.yml</c>: IsLabScale=false, PolicyEngine Core.Recruitment=Shadow, approvals always
/// start, no admin bypass). 25a/25b prove the Production values on that profile and record the active profile in their
/// checks (on the lab profile, as in the CI acceptance job, they still verify that the approval starts and completes);
/// 25c–25h hold on every profile. Refusals
/// are asserted on database state (flash messages are not observable through the gateway) and, where the panel shows it,
/// on the server-computed block reason.
/// </summary>
[RequiredAcceptance]
public class AcHcm25ProductionApprovalTests
{
    private const string Ac = "AC-HCM-25";
    private const string Rationale = "ثبت للمراجع المستقل أن قرار الفرز لم يراعِ المؤهلات المرفقة بالطلب (PLAN-25)";
    private static readonly string[] Approvers = ["central-a", "br1-mgr", "a-management-approver"];
    private const string ApproverRoleMessage = "هذه الخطوة تتطلب دور «hr-management-approver».";
    private const string ComplianceRoleMessage = "هذه الخطوة تتطلب دور «compliance».";

    [SkippableFact]
    public async Task Ac25_a_requisition_submit_starts_the_workflow_and_two_approvals_approve_it_with_production_settings()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "25a", "AC-HCM-25a prod-like: requisition submit → WorkflowInstanceId → two approvals → Approved", Approvers);
        s.Steps("""
            Profile: prod-like (NationalScope:IsLabScale=false, PolicyEngine:Core.Recruitment=Shadow, Recruitment:Approvals:AlwaysStartWorkflow=true);
            the active profile is recorded in before.json / db-checks.json.
            1. central-a creates a requisition on a free AC33-POOL position and submits it → PendingApproval with a
               RECRUITMENT-REQUISITION-APPROVAL instance (before PLAN-25: no instance — G-02).
            2. br1-mgr approves step 1, a-management-approver step 2 → Approved, instance Completed.
            """);
        var profile = await ProfileAsync();

        var id = await CreateRequisitionAsync(s, "25a");
        s.Recorder.WriteBefore(new { requisitionId = id, state = await RequisitionStateAsync(id), profile });
        await s["central-a"].PostAsync("25a-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{id}", ReqDetails(id), []);
        var submitted = (await RequisitionStateAsync(id)).Split('|');
        Assert.Equal("PendingApproval", submitted[0]);
        Assert.True(Guid.TryParse(submitted[1], out _), "25a: no workflow instance after submit with the Production settings (G-02).");
        Assert.Equal("RECRUITMENT-REQUISITION-APPROVAL", await WorkflowCodeAsync(submitted[1]));

        await ApproveRequisitionAsync(s["br1-mgr"], "25a-approve-1", id);
        await ApproveRequisitionAsync(s["a-management-approver"], "25a-approve-2", id);
        var approved = await Sql(
            "SELECT r.\"Status\" || '|' || i.\"Status\" FROM \"RecruitmentRequisitions\" r " +
            $"JOIN \"WorkflowInstances\" i ON i.\"Id\"='{submitted[1]}' WHERE r.\"Id\"='{id}';");
        Assert.Equal("Approved|Completed", approved);

        await s.AuditAsync(id);
        s.Checks(new { requisitionId = id, workflowInstanceId = submitted[1], approved, profile });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac25_b_offer_two_approvals_issue_it_with_production_settings()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "25b", "AC-HCM-25b prod-like: offer submit → two approvals → Issued", Approvers);
        s.Steps("""
            Profile: prod-like (recorded in db-checks.json). Precondition (test-only SQL, ranking is AC-HCM-26): an HR application on a published pool requisition is Ranked 1.
            1. central-a creates the offer at the G5 mid salary and submits it → PendingApproval with RECRUITMENT-OFFER-APPROVAL at step 1.
            2. br1-mgr approves step 1, a-management-approver step 2 → Issued.
            """);
        var profile = await ProfileAsync();

        var reqId = await PublishRequisitionAsync(s, "25b");
        var appId = await CreateApplicationAsync(s, "25b", reqId);
        await Sql(
            $"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppRanked}, \"Rank\"=1, \"AssessmentScore\"=90, \"RankedAt\"=now(), " +
            $"\"ConflictOfInterestDeclared\"=true WHERE \"Id\"='{appId}';");
        var salary = await MidSalaryAsync(s);
        await s["central-a"].PostAsync("25b-create-offer", "ui/hr-core/RecruitmentApplications/CreateOffer", AppDetails(appId), OfferForm(appId, salary));
        var offerId = await Sql($"SELECT \"Id\" FROM \"OfferLetters\" WHERE \"RecruitmentApplicationId\"='{appId}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(offerId, out _), "25b: the offer was not created.");
        s.Recorder.WriteBefore(new { requisitionId = reqId, applicationId = appId, offerId, precondition = "Ranked 1 by test-only SQL" });

        await s["central-a"].PostAsync("25b-submit", "ui/hr-core/OfferLetters/SubmitOffer", OfferDetails(offerId), [new("id", offerId)]);
        var state = (await OfferStateAsync(offerId)).Split('|');
        Assert.Equal([OfferPending.ToString(CultureInfo.InvariantCulture), "1"], [state[0], state[2]]);
        Assert.Equal("RECRUITMENT-OFFER-APPROVAL", await WorkflowCodeAsync(state[1]));
        await ApproveOfferAsync(s["br1-mgr"], "25b-approve-1", offerId);
        await ApproveOfferAsync(s["a-management-approver"], "25b-approve-2", offerId);
        Assert.Equal(OfferIssued.ToString(CultureInfo.InvariantCulture), (await OfferStateAsync(offerId)).Split('|')[0]);

        await s.AuditAsync(offerId, appId);
        s.Checks(new { offerId, workflowInstanceId = state[1], issued = true, profile });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac25_c_user_without_the_approver_role_cannot_approve_step_two_on_the_server()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "25c", "AC-HCM-25c server-side step role: hr-core POST of step 2 refused, state unchanged", Approvers);
        var req = RecruitmentStates.Load().Requisitions.PendingStep2;
        var id = req.Id.ToString("D");
        s.Steps($"""
            Seed: {req.Number} (PendingApproval, step 2 «اعتماد إداري», role hr-management-approver).
            1. br1-mgr (hr-core + branch-manager, in scope, not an approver) opens Details: the server-computed panel shows
               «{ApproverRoleMessage}» and no approve form.
            2. Negative: br1-mgr POSTs ApproveWorkflow and RejectWorkflow directly → both refused by WorkflowStageGuard; status,
               step and history unchanged (before PLAN-25 the role was checked in the UI only — G-04).
            """);

        var before = await RequisitionStateAsync(id);
        AcceptanceGate.RequireData(s.Recorder, before == $"PendingApproval|{req.WorkflowInstanceId:D}|2|InProgress",
            "حالة الطلب المبذور في الخطوة 2 تغيّرت — شغّل scripts/acceptance-reset.ps1",
            $"Seeded {req.Number} is no longer PendingApproval at step 2 ({before}) — run scripts/acceptance-reset.ps1.");
        var history = await Sql($"SELECT \"StepHistoryJson\" FROM \"WorkflowInstances\" WHERE \"Id\"='{req.WorkflowInstanceId:D}';");
        s.Recorder.WriteBefore(new { requisitionId = id, state = before, history });

        var page = await s["br1-mgr"].GetAsync("25c-br1-details", ReqDetails(id));
        Assert.Contains(ApproverRoleMessage, page, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveWorkflow", page, StringComparison.Ordinal);
        await s["br1-mgr"].PostAsync("25c-br1-post-approve", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{id}", ReqDetails(id),
            [new("RequisitionId", id), new("Comments", "PLAN-25 25c wrong role")]);
        await s["br1-mgr"].PostAsync("25c-br1-post-reject", $"ui/hr-core/RecruitmentRequisitions/RejectWorkflow/{id}", ReqDetails(id),
            [new("RequisitionId", id), new("Comments", "PLAN-25 25c wrong role")]);

        Assert.Equal(before, await RequisitionStateAsync(id));
        Assert.Equal(history, await Sql($"SELECT \"StepHistoryJson\" FROM \"WorkflowInstances\" WHERE \"Id\"='{req.WorkflowInstanceId:D}';"));

        await s.AuditAsync(id);
        s.Checks(new { requisitionId = id, before, after = before, expectedRefusal = ApproverRoleMessage });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac25_d_committee_quorum_step_needs_three_active_members_on_the_server()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "25d", "AC-HCM-25d committee quorum on the server: 2 active members refused, 4 approved", Approvers);
        var states = RecruitmentStates.Load();
        s.Steps($"""
            1. central-a: published pool requisition, an Assessed HR application (test-only SQL), a Ranking session with the
               seeded roster (chair + 3, session {states.Committee.ReadySessionId:D}), unanimous votes, a decision, submit.
            2. br1-mgr approves step 1 → quorum step (2).
            3. Negative: two members are recused (test-only SQL) → 2 active; br1-mgr POSTs step 2 → refused
               «النصاب غير مكتمل…» (CommitteeApprovalGate), still step 2; the panel shows the same reason.
            4. Positive: the recusals are reverted → br1-mgr approves step 2 → step 3; a-management-approver approves → ApprovedAt.
            """);

        var roster = (await Sql(
                "SELECT string_agg(\"EmployeeId\"::text || ':' || \"Role\", ',' ORDER BY \"Role\", \"Id\") FROM \"SelectionCommitteeMembers\" " +
                $"WHERE \"SessionId\"='{states.Committee.ReadySessionId:D}';"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Split(':')).ToList();
        AcceptanceGate.RequireData(s.Recorder, roster.Count == 4,
            "قائمة أعضاء اللجنة المبذورة غير مكتملة", "The seeded committee roster (chair + 3) is missing — run scripts/acceptance-reset.ps1.");

        var reqId = await PublishRequisitionAsync(s, "25d");
        var appId = await CreateApplicationAsync(s, "25d", reqId);
        await Sql($"UPDATE \"RecruitmentApplications\" SET \"Status\"={AppAssessed}, \"AssessmentScore\"=84, \"ConflictOfInterestDeclared\"=true WHERE \"Id\"='{appId}';");
        await s["central-a"].PostAsync("25d-create-session", "ui/hr-core/SelectionCommittees/CreateSession",
            $"ui/hr-core/SelectionCommittees?requisitionId={reqId}",
        [
            new("requisitionId", reqId), new("committeeDate", DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("findings", "جلسة ترتيب PLAN-25 25d"), new("purpose", "Ranking")
        ]);
        var session = await Sql($"SELECT \"Id\" FROM \"SelectionCommitteeSessions\" WHERE \"RequisitionId\"='{reqId}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(session, out _), "25d: committee session was not created.");
        foreach (var member in roster)
            await s["central-a"].PostAsync($"25d-add-{member[0][..8]}", "ui/hr-core/SelectionCommittees/AddMember", SessionDetails(session),
                [new("sessionId", session), new("employeeId", member[0]), new("role", member[1]), new("hasConflictOfInterest", "false")]);
        var members = (await Sql($"SELECT string_agg(\"Id\"::text, ',' ORDER BY \"Role\" DESC, \"Id\") FROM \"SelectionCommitteeMembers\" WHERE \"SessionId\"='{session}';"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, members.Length);
        foreach (var member in members)
            await s["central-a"].PostAsync($"25d-vote-{member[..8]}", "ui/hr-core/SelectionCommittees/RecordVote", SessionDetails(session),
                [new("sessionId", session), new("memberId", member), new("applicationId", appId), new("vote", "Agree")]);
        await s["central-a"].PostAsync("25d-decision", "ui/hr-core/SelectionCommittees/UpsertDecision", SessionDetails(session),
        [
            new("sessionId", session), new("applicationId", appId), new("recommendation", "Recommend"), new("proposedRank", "1"),
            new("rationale", "توصية لجنة الاختيار وفق الدرجات")
        ]);
        await s["central-a"].PostAsync("25d-submit", $"ui/hr-core/SelectionCommittees/Submit/{session}", SessionDetails(session), [new("id", session)]);
        Assert.Equal(1, await CommitteeStepAsync(session));
        s.Recorder.WriteBefore(new { requisitionId = reqId, applicationId = appId, session, members });

        await ApproveCommitteeAsync(s["br1-mgr"], "25d-step-1", session);
        Assert.Equal(2, await CommitteeStepAsync(session));

        // Two of the three non-chair members recuse (test-only SQL): 2 active members remain.
        var recused = members.Where(m => m != members[0]).Take(2).ToList();
        await Sql($"UPDATE \"SelectionCommitteeMembers\" SET \"Recused\"=true WHERE \"Id\" IN ('{string.Join("','", recused)}');");
        try
        {
            var page = await s["br1-mgr"].GetAsync("25d-br1-details-2-active", SessionDetails(session));
            Assert.Contains("النصاب غير مكتمل", page, StringComparison.Ordinal);
            await ApproveCommitteeAsync(s["br1-mgr"], "25d-step-2-without-quorum", session);
            Assert.Equal(2, await CommitteeStepAsync(session));
        }
        finally
        {
            await Sql($"UPDATE \"SelectionCommitteeMembers\" SET \"Recused\"=false WHERE \"Id\" IN ('{string.Join("','", recused)}');");
        }

        await ApproveCommitteeAsync(s["br1-mgr"], "25d-step-2-with-quorum", session);
        Assert.Equal(3, await CommitteeStepAsync(session));
        await ApproveCommitteeAsync(s["a-management-approver"], "25d-step-3", session);
        Assert.Equal("true", await Sql($"SELECT (\"ApprovedAt\" IS NOT NULL)::text FROM \"SelectionCommitteeSessions\" WHERE \"Id\"='{session}';"));

        await s.AuditAsync(session, reqId, appId);
        s.Checks(new { session, refusedWithTwoActive = true, approvedWithFourActive = true, expectedRefusal = "النصاب غير مكتمل" });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac25_e_only_the_assigned_compliance_reviewer_decides_an_appeal_on_the_server()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "25e", "AC-HCM-25e appeal: assigned a-compliance decides; other compliance (gate) and hr-core (role) refused",
            "central-a", "a-compliance", "a-compliance-2");
        var compliance = AcceptancePersonaCatalog.Get("a-compliance").Employee!.Id.ToString("D");
        var compliance2 = AcceptancePersonaCatalog.Get("a-compliance-2").Employee!.Id.ToString("D");
        var reqId = RecruitmentStates.Load().Requisitions.Published.Id.ToString("D");
        s.Steps($"""
            1. central-a enters an application on the seeded published requisition, rejects it, files an appeal and assigns a-compliance.
            2. Negative (gate): a-compliance-2 POSTs a Decide for it → refused (AppealApprovalGate: not the assigned reviewer).
            3. Negative (role): central-a (hr-core, allowed into the appeal pages) sees the server-computed reason
               «{ComplianceRoleMessage}» on the appeal and its Decide POST is refused — the review step requires «compliance»
               (the actor role is no longer hard-coded to Compliance).
            4. Positive: a-compliance decides «Upheld» → Decided, instance Completed.
            """);

        var appId = await CreateApplicationAsync(s, "25e", reqId);
        await s["central-a"].PostAsync("25e-reject", "ui/hr-core/RecruitmentApplications/SetStatus", AppDetails(appId),
            [new("id", appId), new("status", "Rejected"), new("rejectionNote", "لا يستوفي شرط المؤهل (PLAN-25)")]);
        await s["central-a"].PostAsync("25e-file", "ui/hr-core/RecruitmentApplications/FileAppeal", AppDetails(appId),
            [new("id", appId), new("reason", "يعترض المرشح على قرار الفرز (PLAN-25)")]);
        var appeal = await Sql($"SELECT \"Id\" FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{appId}' AND \"Status\"={AppealPending};");
        Assert.True(Guid.TryParse(appeal, out _), "25e: appeal was not filed.");
        await s["central-a"].PostAsync("25e-assign", "ui/hr-core/RecruitmentAppeals/AssignReviewer", AppealDetails(appeal),
            [new("id", appeal), new("reviewerEmployeeId", compliance)]);
        var instance = await Sql($"SELECT COALESCE(\"WorkflowInstanceId\"::text,'') FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal}';");
        Assert.True(Guid.TryParse(instance, out _), "25e: no review workflow after assignment.");
        s.Recorder.WriteBefore(new { appeal, applicationId = appId, instance, reviewer = compliance });

        // Gate: a second compliance user (their own appeal page supplies the antiforgery token).
        var app2 = await CreateApplicationAsync(s, "25e-2", reqId);
        await s["central-a"].PostAsync("25e-2-reject", "ui/hr-core/RecruitmentApplications/SetStatus", AppDetails(app2),
            [new("id", app2), new("status", "Rejected"), new("rejectionNote", "لا يستوفي شرط المؤهل (PLAN-25)")]);
        await s["central-a"].PostAsync("25e-2-file", "ui/hr-core/RecruitmentApplications/FileAppeal", AppDetails(app2),
            [new("id", app2), new("reason", "يعترض المرشح على قرار الفرز (PLAN-25)")]);
        var appeal2 = await Sql($"SELECT \"Id\" FROM \"RecruitmentAppeals\" WHERE \"RecruitmentApplicationId\"='{app2}' AND \"Status\"={AppealPending};");
        await s["central-a"].PostAsync("25e-2-assign", "ui/hr-core/RecruitmentAppeals/AssignReviewer", AppealDetails(appeal2),
            [new("id", appeal2), new("reviewerEmployeeId", compliance2)]);
        await s["a-compliance-2"].PostWithStatusAsync("25e-other-compliance-decides", "ui/hr-core/RecruitmentAppeals/Decide", AppealDetails(appeal2),
            [new("id", appeal), new("outcome", "Upheld"), new("rationale", Rationale)]);
        Assert.Equal($"{AppealPending}|", await Sql($"SELECT \"Status\"::text || '|' || COALESCE(\"Outcome\",'') FROM \"RecruitmentAppeals\" WHERE \"Id\"='{appeal}';"));

        // Role: hr-core on the compliance step.
        var hrPage = await s["central-a"].GetAsync("25e-hr-core-appeal-details", AppealDetails(appeal));
        Assert.Contains(ComplianceRoleMessage, hrPage, StringComparison.Ordinal);
        await s["central-a"].PostWithStatusAsync("25e-hr-core-decides", "ui/hr-core/RecruitmentAppeals/Decide", AppealDetails(appeal),
            [new("id", appeal), new("outcome", "Upheld"), new("rationale", Rationale)]);
        Assert.Equal($"{AppealPending}|InProgress|1", await Sql(
            "SELECT a.\"Status\"::text || '|' || i.\"Status\" || '|' || i.\"CurrentStepLevel\" FROM \"RecruitmentAppeals\" a " +
            $"JOIN \"WorkflowInstances\" i ON i.\"Id\"=a.\"WorkflowInstanceId\" WHERE a.\"Id\"='{appeal}';"));

        await s["a-compliance"].PostAsync("25e-decide", "ui/hr-core/RecruitmentAppeals/Decide", AppealDetails(appeal),
            [new("id", appeal), new("outcome", "Upheld"), new("rationale", Rationale)]);
        Assert.Equal($"{AppealDecided}|Upheld|Completed", await Sql(
            "SELECT a.\"Status\"::text || '|' || a.\"Outcome\" || '|' || i.\"Status\" FROM \"RecruitmentAppeals\" a " +
            $"JOIN \"WorkflowInstances\" i ON i.\"Id\"='{instance}' WHERE a.\"Id\"='{appeal}';"));
        await s["a-compliance-2"].PostAsync("25e-2-cleanup-decide", "ui/hr-core/RecruitmentAppeals/Decide", AppealDetails(appeal2),
            [new("id", appeal2), new("outcome", "Dismissed"), new("rationale", Rationale)]);

        await s.AuditAsync(appeal, appeal2, appId, app2);
        s.Checks(new { appeal, otherReviewerRefused = true, hrCoreRefused = ComplianceRoleMessage, decided = "Upheld" });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac25_f_management_approver_cannot_edit_a_public_posting()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "25f", "AC-HCM-25f a-management-approver POST PublicJobPostings/Edit → 403", "central-a", "a-management-approver");
        var posting = RecruitmentStates.Load().Requisitions.Published.PostingId!.Value.ToString("D");
        s.Steps($"""
            Seed: public posting {posting}.
            1. a-management-approver (Module.HrCore lets the role into HR.Core) opens the posting Details (read is allowed).
            2. Negative: POST PublicJobPostings/Edit → 403 (explicit [Authorize(Policy = HrCoreWrite)] since PLAN-25, G-15;
               the gateway's HrCoreWrite rule for mutating requests refuses it as well). The posting is unchanged.
            """);

        var before = await Sql($"SELECT \"Title\" || '|' || COALESCE(\"ClosingDate\"::text,'') FROM \"PublicJobPostings\" WHERE \"Id\"='{posting}';");
        s.Recorder.WriteBefore(new { posting, before });
        await s["a-management-approver"].GetAsync("25f-details", $"ui/hr-core/PublicJobPostings/Details/{posting}");
        var (status, _) = await s["a-management-approver"].PostWithStatusAsync("25f-edit", $"ui/hr-core/PublicJobPostings/Edit/{posting}",
            $"ui/hr-core/PublicJobPostings/Details/{posting}",
            [new("Id", posting), new("Title", "PLAN-25 25f"), new("Entity", "جهة"), new("IsPublished", "true")]);
        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(before, await Sql($"SELECT \"Title\" || '|' || COALESCE(\"ClosingDate\"::text,'') FROM \"PublicJobPostings\" WHERE \"Id\"='{posting}';"));

        await s.AuditAsync(posting);
        s.Checks(new { posting, status = (int)status, unchanged = true });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac25_g_hire_starts_no_recruitment_hire_workflow()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "25g", "AC-HCM-25g RECRUITMENT-HIRE retired: rule neutralised, definition unpublished, no instances", "central-a");
        s.Steps("""
            Live facts on the running stack (patched on start-up by WorkflowPackSeeder.RetireAsync and
            PolicyEngineDomainSeeds.DeactivateRuleIfPresentAsync):
            1. R-REC-HIRE has no effect («[]») and its name is marked «[معطّلة]»; R-REC-ORG and R-REC-HIRE-OFFER remain.
            2. RECRUITMENT-HIRE is unpublished with ValidTo set.
            3. No RecruitmentApplication workflow instance is in progress (hires on this stack — AC-HCM-22a/30/33c — started none).
            The Enforce-mode hire itself (fresh DB, pre-PLAN-25 DB with rule + published definition + binding, static fallback)
            is proven by Hcm025RecruitmentHireRetiredTests (unit) — Enforce is not the deployed mode (Production = Shadow).
            """);

        var rule = await Sql("SELECT \"ThenJson\" || '|' || \"Name\" FROM \"PolicyRules\" WHERE \"RuleKey\"='R-REC-HIRE';");
        var kept = await Scalar("SELECT COUNT(*) FROM \"PolicyRules\" WHERE \"RuleKey\" IN ('R-REC-ORG','R-REC-HIRE-OFFER');");
        var definition = await Sql("SELECT \"IsPublished\"::text || '|' || (\"ValidTo\" IS NOT NULL)::text FROM \"WorkflowDefinitions\" WHERE \"Code\"='RECRUITMENT-HIRE';");
        var inProgress = await Scalar("SELECT COUNT(*) FROM \"WorkflowInstances\" WHERE \"EntityType\"='RecruitmentApplication' AND \"Status\"='InProgress';");
        var hires = await Scalar($"SELECT COUNT(*) FROM \"RecruitmentApplications\" WHERE \"Status\"={4};");
        s.Recorder.WriteBefore(new { rule, kept, definition, inProgress, hires });

        Assert.True(rule.Length == 0 || rule.StartsWith("[]|[معطّلة]", StringComparison.Ordinal), $"R-REC-HIRE still active: {rule}");
        Assert.Equal(2, kept);
        Assert.Equal("false|true", definition);
        Assert.Equal(0, inProgress);

        s.Checks(new { rule, kept, definition, inProgress, hiresOnStack = hires });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac25_h_repair_script_lists_then_repairs_seeded_stuck_rows()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "25h", "AC-HCM-25h repair script: dry run lists seeded stuck rows; -Apply starts their workflows", "central-a", "br1-mgr");
        s.Steps("""
            Seeded on purpose (test-only SQL, as a pre-PLAN-25 build left them): a pool requisition PendingApproval without a
            workflow, and an InProgress RECRUITMENT-HIRE instance (orphan) on an application.
            1. scripts/hcm025-repair-stuck-approvals.ps1 (dry run, acc-a-directory-checker = hr-admin, HrCoreDelete) lists both; nothing changes.
            2. With -Apply: the requisition gets RECRUITMENT-REQUISITION-APPROVAL (still PendingApproval), the orphan is Cancelled,
               each with an audit row; a second dry run no longer lists them. Clean-up: the requisition is rejected and cancelled.
            """);

        var reqId = await CreateRequisitionAsync(s, "25h");
        await Sql($"UPDATE \"RecruitmentRequisitions\" SET \"Status\"='PendingApproval', \"WorkflowInstanceId\"=NULL WHERE \"Id\"='{reqId}';");
        var appId = RecruitmentStates.Load().Applications.Submitted.ToString("D");
        var orphan = Guid.NewGuid().ToString("D");
        await Sql(
            "INSERT INTO \"WorkflowInstances\" (\"Id\",\"TenantId\",\"CreatedAt\",\"UpdatedAt\",\"DefinitionId\",\"DefinitionVersion\",\"EntityType\",\"EntityId\"," +
            "\"CurrentStepLevel\",\"Status\",\"StepHistoryJson\",\"InitiatedBy\",\"StartedAt\") " +
            $"SELECT '{orphan}', '{AcceptanceSeedIds.AgencyAId}', now(), now(), d.\"Id\", d.\"Version\", 'RecruitmentApplication', '{appId}', 1, 'InProgress', '[]', NULL, now() " +
            "FROM \"WorkflowDefinitions\" d WHERE d.\"Code\"='RECRUITMENT-HIRE' ORDER BY d.\"Version\" DESC LIMIT 1;");
        Assert.Equal("InProgress", await Sql($"SELECT \"Status\" FROM \"WorkflowInstances\" WHERE \"Id\"='{orphan}';"));
        s.Recorder.WriteBefore(new { requisitionId = reqId, orphanInstance = orphan, applicationId = appId });

        var dry = await RunRepairAsync(s, apply: false);
        Assert.Contains(reqId, dry, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(appId, dry, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("PendingApproval|", await Sql($"SELECT \"Status\" || '|' || COALESCE(\"WorkflowInstanceId\"::text,'') FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));

        var applied = await RunRepairAsync(s, apply: true);
        var state = (await RequisitionStateAsync(reqId)).Split('|');
        Assert.Equal("PendingApproval", state[0]);
        Assert.Equal("RECRUITMENT-REQUISITION-APPROVAL", await WorkflowCodeAsync(state[1]));
        Assert.Equal("Cancelled", await Sql($"SELECT \"Status\" FROM \"WorkflowInstances\" WHERE \"Id\"='{orphan}';"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{reqId}' AND \"Action\"='ApprovalWorkflowRestarted';"));
        var again = await RunRepairAsync(s, apply: false);
        Assert.DoesNotContain(reqId, again, StringComparison.OrdinalIgnoreCase);

        await s["br1-mgr"].PostAsync("25h-cleanup-reject", $"ui/hr-core/RecruitmentRequisitions/RejectWorkflow/{reqId}", ReqDetails(reqId),
            [new("RequisitionId", reqId), new("Comments", "تنظيف بعد سيناريو قبول PLAN-25")]);
        await s["central-a"].PostAsync("25h-cleanup-cancel", $"ui/hr-core/RecruitmentRequisitions/Cancel/{reqId}", ReqDetails(reqId),
            [new("reason", "تنظيف بعد سيناريو قبول PLAN-25")]);

        await s.AuditAsync(reqId);
        s.Checks(new { requisitionId = reqId, orphan, dryRunListed = true, applied = applied.Length > 300 ? applied[..300] : applied });
        s.Recorder.WriteResult("Pass");
    }

    // ---------------------------------------------------------------- helpers

    private static Task<long> CommitteeStepAsync(string session) =>
        Scalar("SELECT i.\"CurrentStepLevel\" FROM \"SelectionCommitteeSessions\" s JOIN \"WorkflowInstances\" i ON i.\"Id\"=s.\"WorkflowInstanceId\" " +
               $"WHERE s.\"Id\"='{session}';");

    private static Task ApproveCommitteeAsync(AcceptanceUi ui, string step, string sessionId) =>
        ui.PostAsync(step, $"ui/hr-core/SelectionCommittees/ApproveWorkflow/{sessionId}", SessionDetails(sessionId),
            [new("SessionId", sessionId), new("Comments", $"PLAN-25 {step}")]);

    /// <summary>Which recruitment profile hr-core-web runs: «prod-like» only with the Production values of decision 10.</summary>
    private static async Task<string> ProfileAsync()
    {
        var labScale = await HrCoreEnvAsync("NationalScope__IsLabScale");
        var policy = await HrCoreEnvAsync("PolicyEngine__Core.Recruitment");
        return labScale == "false" && policy == "Shadow"
            ? "prod-like (IsLabScale=false, Core.Recruitment=Shadow)"
            : $"lab (IsLabScale={labScale ?? "unset"}, Core.Recruitment={policy ?? "unset"})";
    }

    private static async Task<string?> HrCoreEnvAsync(string name)
    {
        var (_, names) = await RunAsync("docker", "ps", "--filter", "label=com.docker.compose.project=hrm-acceptance",
            "--filter", "label=com.docker.compose.service=hr-core-web", "--format", "{{.Names}}");
        var container = names.Trim().Split('\n')[0].Trim();
        if (container.Length == 0)
            return null;
        var (_, env) = await RunAsync("docker", "inspect", "--format", "{{range .Config.Env}}{{println .}}{{end}}", container);
        return env.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
    }

    private static async Task<string> RunRepairAsync(RecruitmentSession s, bool apply)
    {
        var script = Path.Combine(AcceptanceHarness.RepoRoot, "scripts", "hcm025-repair-stuck-approvals.ps1");
        var outDir = Path.Combine(AcceptanceHarness.RepoRoot, "acceptance-evidence", Ac, "repair");
        List<string> args = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-OutDir", outDir];
        if (apply)
            args.Add("-Apply");
        var (exit, output) = await RunAsync("powershell", [.. args]);
        Assert.True(exit == 0, $"hcm025-repair-stuck-approvals.ps1 failed ({exit}): {output}");
        return output;
    }

    private static async Task<(int Exit, string Output)> RunAsync(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, (await stdout) + (await stderr));
    }
}
