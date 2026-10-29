using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-HCM-21a–d (PLAN-21, rewritten in PLAN-33): requisition approval through the two-step
/// RECRUITMENT-REQUISITION-APPROVAL workflow. The SoD scenario (21b) uses the seeded PendingApproval requisition of
/// <see cref="RecruitmentStates"/>; scenarios that change state create their own requisition on an AC33-POOL position.
/// Flash messages are not observable through the gateway, so refusals are asserted on database state.
/// </summary>
[RequiredAcceptance]
public class AcHcm21RequisitionApprovalUiTests
{
    private const string Ac = "AC-HCM-21";
    private static readonly string[] Personas = ["central-a", "br1-mgr", "a-management-approver"];

    [SkippableFact]
    public async Task Ac21_a_submit_two_step_approval_then_publish_with_closing_date()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "21a", "AC-HCM-21a submit → br1-mgr step 1 → a-management-approver step 2 → Approved → publish", Personas);
        s.Steps("""
            Personas: central-a (creator), br1-mgr (step 1 «مراجعة HR», role hr-core, RIF scope), a-management-approver (step 2).
            1. central-a creates a draft requisition on a free AC33-POOL position (RIF) and submits it.
            2. PendingApproval with a RECRUITMENT-REQUISITION-APPROVAL instance at step 1.
            3. br1-mgr approves step 1 → step 2; a-management-approver approves step 2 → Approved (instance Completed, ApprovedDate set).
            4. central-a adds criteria v1 and publishes with a closing date 30 days ahead → Published, posting created, closing date stored.
            """);

        var id = await CreateRequisitionAsync(s, "21a");
        s.Recorder.WriteBefore(new { requisitionId = id, state = await RequisitionStateAsync(id) });
        await s["central-a"].PostAsync("21a-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{id}", ReqDetails(id), []);
        var submitted = (await RequisitionStateAsync(id)).Split('|');
        Assert.Equal(["PendingApproval", "1", "InProgress"], [submitted[0], submitted[2], submitted[3]]);
        Assert.Equal("RECRUITMENT-REQUISITION-APPROVAL", await WorkflowCodeAsync(submitted[1]));

        await ApproveRequisitionAsync(s["br1-mgr"], "21a-approve-1", id);
        Assert.Equal("2", (await RequisitionStateAsync(id)).Split('|')[2]);
        await ApproveRequisitionAsync(s["a-management-approver"], "21a-approve-2", id);
        var approved = await Sql(
            "SELECT r.\"Status\" || '|' || (r.\"ApprovedDate\" IS NOT NULL)::text || '|' || i.\"Status\" FROM \"RecruitmentRequisitions\" r " +
            $"JOIN \"WorkflowInstances\" i ON i.\"Id\"='{submitted[1]}' WHERE r.\"Id\"='{id}';");
        Assert.Equal("Approved|true|Completed", approved);

        var closing = ClosingDate(30);
        await s["central-a"].PostAsync("21a-criteria", "ui/hr-core/RecruitmentAssessmentCriteria/Create", ReqDetails(id), CriteriaForm(id));
        await s["central-a"].PostAsync("21a-publish", $"ui/hr-core/RecruitmentRequisitions/Publish/{id}", ReqDetails(id),
            [new("applicationClosingDate", closing)]);
        var published = await Sql(
            "SELECT \"Status\" || '|' || COALESCE(\"ApplicationClosingDate\"::text,'') || '|' || (\"PublicJobPostingId\" IS NOT NULL)::text " +
            $"FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{id}';");
        Assert.Equal($"Published|{closing}|true", published);

        await s.AuditAsync(id);
        s.Checks(new { requisitionId = id, workflowInstanceId = submitted[1], approved, published });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac21_b_creator_sees_no_approve_action_and_direct_post_is_refused()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "21b", "AC-HCM-21b creator cannot approve own requisition (UI and direct POST)", Personas);
        var req = RecruitmentStates.Load().Requisitions.PendingStep1;
        var id = req.Id.ToString("D");
        s.Steps($"""
            Seed: {req.Number} (PendingApproval, step 1 of RECRUITMENT-REQUISITION-APPROVAL, created by central-a).
            1. central-a opens Details: the creator block reason «لا يمكن للمنشئ اعتماد طلبه الخاص.» is shown and there is no approve form.
            2. br1-mgr (control) opens the same Details and does get the approve form.
            3. Negative: central-a POSTs ApproveWorkflow directly → refused; status, step and history unchanged.
            """);

        var before = await RequisitionStateAsync(id);
        AcceptanceGate.RequireData(s.Recorder, before == $"PendingApproval|{req.WorkflowInstanceId:D}|1|InProgress",
            "حالة الطلب المعلق المبذور تغيّرت — شغّل scripts/acceptance-reset.ps1",
            $"Seeded {req.Number} is no longer PendingApproval at step 1 ({before}) — run scripts/acceptance-reset.ps1.");
        s.Recorder.WriteBefore(new { requisitionId = id, state = before });

        var creatorPage = await s["central-a"].GetAsync("21b-creator-details", ReqDetails(id));
        Assert.Contains("لا يمكن للمنشئ اعتماد طلبه الخاص.", creatorPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ApproveWorkflow", creatorPage, StringComparison.Ordinal);
        var approverPage = await s["br1-mgr"].GetAsync("21b-br1-details", ReqDetails(id));
        Assert.Contains("ApproveWorkflow", approverPage, StringComparison.Ordinal);

        await s["central-a"].PostAsync("21b-creator-post-approve", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{id}", ReqDetails(id),
            [new("RequisitionId", id), new("Comments", "PLAN-33 21b self-approval attempt")]);
        var after = await RequisitionStateAsync(id);
        Assert.Equal(before, after);
        Assert.Equal("[]", await Sql($"SELECT \"StepHistoryJson\" FROM \"WorkflowInstances\" WHERE \"Id\"='{req.WorkflowInstanceId:D}';"));

        await s.AuditAsync(id);
        s.Checks(new { requisitionId = id, before, after, expectedRefusal = "لا يمكن للمنشئ اعتماد طلبه الخاص." });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac21_c_reject_returns_requisition_to_draft_with_reason()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "21c", "AC-HCM-21c br1-mgr rejects → Draft with the reason recorded", Personas);
        const string reason = "PLAN-33 21c: المبررات غير كافية لإشغال الشاغر";
        s.Steps($"""
            1. central-a creates and submits a requisition on an AC33-POOL position (PendingApproval, step 1).
            2. br1-mgr rejects with the reason «{reason}».
            3. The requisition is back to Draft, WorkflowInstanceId cleared; the instance is Rejected and its history holds the reason.
            4. Negative: approving the draft afterwards is refused (no workflow) — it stays Draft.
            5. Clean-up: central-a cancels the draft so the pool position is free again.
            """);

        var id = await CreateRequisitionAsync(s, "21c");
        await s["central-a"].PostAsync("21c-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{id}", ReqDetails(id), []);
        var instance = (await RequisitionStateAsync(id)).Split('|')[1];
        Assert.True(Guid.TryParse(instance, out _), "21c: no workflow instance after submit (lab scale).");
        s.Recorder.WriteBefore(new { requisitionId = id, instance, state = await RequisitionStateAsync(id) });

        await s["br1-mgr"].PostAsync("21c-reject", $"ui/hr-core/RecruitmentRequisitions/RejectWorkflow/{id}", ReqDetails(id),
            [new("RequisitionId", id), new("Comments", reason)]);
        Assert.Equal("Draft|||", await RequisitionStateAsync(id));
        var history = await Sql($"SELECT \"Status\" || '|' || \"StepHistoryJson\" FROM \"WorkflowInstances\" WHERE \"Id\"='{instance}';");
        var parts = history.Split('|', 2);
        Assert.Equal("Rejected", parts[0]);
        using (var doc = System.Text.Json.JsonDocument.Parse(parts[1]))
            Assert.Contains(doc.RootElement.EnumerateArray(), e => e.TryGetProperty("Comments", out var c) && c.GetString() == reason);

        await ApproveRequisitionAsync(s["a-management-approver"], "21c-approve-draft", id);
        Assert.StartsWith("Draft|", await RequisitionStateAsync(id));

        await s["central-a"].PostAsync("21c-cleanup-cancel", $"ui/hr-core/RecruitmentRequisitions/Cancel/{id}", ReqDetails(id),
            [new("reason", "تنظيف بعد سيناريو قبول PLAN-33")]);

        await s.AuditAsync(id);
        s.Checks(new { requisitionId = id, instance, history, reason });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Ac21_d_lab_scale_submit_starts_the_approval_workflow()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "21d", "AC-HCM-21d lab scale: submit creates the WorkflowInstanceId", Personas);
        s.Steps("""
            Live stack = acceptance profile (NationalScope:IsLabScale=true, policy engine Off).
            1. central-a submits a new requisition → WorkflowInstanceId set, definition RECRUITMENT-REQUISITION-APPROVAL, InProgress at step 1.
            2. The Details page shows the workflow panel and no «لم يُنشأ مسار اعتماد» warning.
            3. Clean-up: br1-mgr rejects, central-a cancels (frees the pool position).
            The non-lab negative (IsLabScale=false + policy Off → PendingApproval without a workflow and the warning) cannot be
            produced on the shared lab stack; it is proven by the integration test
            src/Tests/HRManagement.IntegrationTests/Hcm033LabScaleBehaviourTests.cs (G-02, PLAN-25).
            """);

        var id = await CreateRequisitionAsync(s, "21d");
        await s["central-a"].PostAsync("21d-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{id}", ReqDetails(id), []);
        var state = (await RequisitionStateAsync(id)).Split('|');
        Assert.Equal("PendingApproval", state[0]);
        Assert.True(Guid.TryParse(state[1], out _), "21d: lab-scale submit did not create a WorkflowInstanceId.");
        Assert.Equal(["1", "InProgress"], [state[2], state[3]]);
        Assert.Equal("RECRUITMENT-REQUISITION-APPROVAL", await WorkflowCodeAsync(state[1]));
        var page = await s["central-a"].GetAsync("21d-details", ReqDetails(id));
        Assert.Contains("مسار الاعتماد", page, StringComparison.Ordinal);
        Assert.DoesNotContain("لم يُنشأ مسار اعتماد", page, StringComparison.Ordinal);

        await s["br1-mgr"].PostAsync("21d-cleanup-reject", $"ui/hr-core/RecruitmentRequisitions/RejectWorkflow/{id}", ReqDetails(id),
            [new("RequisitionId", id), new("Comments", "تنظيف بعد سيناريو قبول PLAN-33")]);
        await s["central-a"].PostAsync("21d-cleanup-cancel", $"ui/hr-core/RecruitmentRequisitions/Cancel/{id}", ReqDetails(id),
            [new("reason", "تنظيف بعد سيناريو قبول PLAN-33")]);

        await s.AuditAsync(id);
        s.Checks(new { requisitionId = id, workflowInstanceId = state[1], nonLab = "Hcm033LabScaleBehaviourTests (integration)" });
        s.Recorder.WriteResult("Pass");
    }
}
