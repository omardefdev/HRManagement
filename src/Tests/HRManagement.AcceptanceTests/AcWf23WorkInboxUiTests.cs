using System.Net;
using System.Text.Json;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-WF-23a–f (PLAN-23, rewritten in PLAN-33): the unified work inbox on the seeded states of
/// <see cref="RecruitmentStates"/> — items created by central-a in RIF: a requisition at step 1, a requisition at step 2,
/// an offer at step 1, an appeal assigned to a-compliance, and a legacy ApprovalRequest (role hr-core). Visibility follows
/// the step role, the org scope, creator SoD and the assigned reviewer. Every check reads; nothing is approved.
/// </summary>
[RequiredAcceptance]
public class AcWf23WorkInboxUiTests
{
    private const string Ac = "AC-WF-23";

    private static string Req(Guid id) => $"/RecruitmentRequisitions/Details/{id:D}";
    private static string Offer(Guid id) => $"/OfferLetters/Details/{id:D}";
    private static string Appeal(Guid id) => $"/RecruitmentAppeals/Details/{id:D}";
    private static string Legacy(Guid id) => $"/Approvals/Review/{id:D}";

    [SkippableFact]
    public async Task Wf23_a_branch_manager_lists_step_one_items_in_scope()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "23a", "AC-WF-23a br1-mgr lists the step-1 items of RIF", "br1-mgr");
        var st = RecruitmentStates.Load();
        s.Steps($"""
            br1-mgr (hr-core + branch-manager, RIF) opens «صندوق العمل».
            Expected: {st.Requisitions.PendingStep1.Number} (step 1, role hr-core) and {st.Offers.PendingApproval.Number} (step 1) are listed;
            {st.Requisitions.PendingStep2.Number} (step 2, role hr-management-approver) is not.
            """);

        var inbox = await InboxAsync(s["br1-mgr"], "23a-inbox");
        Assert.True(inbox.Has(Req(st.Requisitions.PendingStep1.Id)), "br1-mgr should see the step-1 requisition.");
        Assert.True(inbox.Has(Offer(st.Offers.PendingApproval.Id)), "br1-mgr should see the step-1 offer.");
        Assert.False(inbox.Has(Req(st.Requisitions.PendingStep2.Id)), "Step 2 needs hr-management-approver.");
        Assert.False(inbox.Has(Appeal(st.Appeal.Id)), "Appeals belong to the assigned compliance reviewer.");

        s.Checks(new { persona = "br1-mgr", links = inbox.Links, pendingBadge = inbox.PendingBadge });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Wf23_b_management_approver_sees_only_its_steps()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "23b", "AC-WF-23b a-management-approver sees only step-2 items", "a-management-approver");
        var st = RecruitmentStates.Load();
        s.Steps($"""
            a-management-approver (role hr-management-approver, no hr-core).
            Expected: {st.Requisitions.PendingStep2.Number} (step 2) is listed; the step-1 requisition and step-1 offer are not;
            the legacy request (role hr-core) is not.
            """);

        var inbox = await InboxAsync(s["a-management-approver"], "23b-inbox");
        Assert.True(inbox.Has(Req(st.Requisitions.PendingStep2.Id)), "The approver should see the step-2 requisition.");
        Assert.False(inbox.Has(Req(st.Requisitions.PendingStep1.Id)), "Step 1 needs hr-core.");
        Assert.False(inbox.Has(Offer(st.Offers.PendingApproval.Id)), "Offer step 1 needs hr-core.");
        Assert.False(inbox.Has(Legacy(st.LegacyApprovalRequest.Id)), "The legacy step needs hr-core.");

        s.Checks(new { persona = "a-management-approver", links = inbox.Links });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Wf23_c_creator_gets_no_actionable_items_of_its_own()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "23c", "AC-WF-23c central-a gets none of the items it created", "central-a");
        var st = RecruitmentStates.Load();
        s.Steps("""
            central-a (hr-core, whole agency) created every seeded item. Expected: none of them is listed (creator SoD),
            although its role would match step 1.
            """);

        var inbox = await InboxAsync(s["central-a"], "23c-inbox");
        foreach (var own in new[]
                 {
                     Req(st.Requisitions.PendingStep1.Id), Req(st.Requisitions.PendingStep2.Id), Offer(st.Offers.PendingApproval.Id),
                     Appeal(st.Appeal.Id), Legacy(st.LegacyApprovalRequest.Id)
                 })
            Assert.False(inbox.Has(own), $"central-a must not get its own item {own}.");

        s.Checks(new { persona = "central-a", links = inbox.Links });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Wf23_d_compliance_reviewer_sees_only_its_appeal()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "23d", "AC-WF-23d a-compliance sees only the appeal assigned to it", "a-compliance", "a-compliance-2");
        var st = RecruitmentStates.Load();
        s.Steps("""
            a-compliance (role compliance only) is the assigned reviewer of the seeded appeal; a-compliance-2 is not.
            Expected: a-compliance lists the appeal and only appeals; a-compliance-2 does not list it.
            """);

        var inbox = await InboxAsync(s["a-compliance"], "23d-inbox");
        Assert.True(inbox.Has(Appeal(st.Appeal.Id)), "The assigned reviewer should see the appeal.");
        Assert.All(inbox.Links, l => Assert.StartsWith("/RecruitmentAppeals/Details/", l, StringComparison.Ordinal));
        var other = await InboxAsync(s["a-compliance-2"], "23d-inbox-other");
        Assert.False(other.Has(Appeal(st.Appeal.Id)), "A non-assigned reviewer must not see the appeal.");

        s.Checks(new { assigned = inbox.Links, other = other.Links });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Wf23_e_pending_count_equals_the_actionable_rows()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "23e", "AC-WF-23e stats count == inbox rows (br1-mgr)", "br1-mgr");
        s.Steps("""
            For br1-mgr: GET ui/hr-core/api/stats/pending-approvals → {count, legacyCount, workflowCount}, then the inbox.
            Expected: count == workflowCount + legacyCount == number of item rows == the «معلّق» badge, and count ≥ 3
            (two seeded workflow items and the legacy request are actionable for br1-mgr).
            """);

        var (status, json) = await s["br1-mgr"].GetWithStatusAsync("23e-stats", "ui/hr-core/api/stats/pending-approvals");
        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.GetProperty("count").GetInt32();
        var workflow = doc.RootElement.GetProperty("workflowCount").GetInt32();
        var legacy = doc.RootElement.GetProperty("legacyCount").GetInt32();
        var inbox = await InboxAsync(s["br1-mgr"], "23e-inbox");

        Assert.Equal(count, workflow + legacy);
        Assert.Equal(count, inbox.Links.Count);
        Assert.Equal(count, inbox.PendingBadge);
        Assert.True(count >= 3, $"Expected at least the 3 seeded actionable items, got {count}.");

        s.Checks(new { count, workflow, legacy, rows = inbox.Links.Count, badge = inbox.PendingBadge });
        s.Recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Wf23_f_legacy_approval_request_is_visible_with_its_link()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "23f", "AC-WF-23f legacy ApprovalRequest in the unified inbox", "br1-mgr");
        var st = RecruitmentStates.Load();
        s.Steps($"""
            Seed: legacy ApprovalRequest {st.LegacyApprovalRequest.Id:D} (Pending, step 1 role {st.LegacyApprovalRequest.RoleRequired}, requested by central-a).
            Expected: br1-mgr's inbox lists it with the «legacy» badge and the /Approvals/Review/<id> link, and the review page opens.
            """);

        var inbox = await InboxAsync(s["br1-mgr"], "23f-inbox");
        Assert.True(inbox.Has(Legacy(st.LegacyApprovalRequest.Id)), "The legacy request should be listed.");
        Assert.Contains(">legacy<", inbox.Html, StringComparison.Ordinal);
        var review = await s["br1-mgr"].GetAsync("23f-review", $"ui/hr-core/Approvals/Review/{st.LegacyApprovalRequest.Id:D}");
        Assert.Contains("ApprovedCV", review, StringComparison.OrdinalIgnoreCase);

        s.Checks(new { legacyId = st.LegacyApprovalRequest.Id, listed = true });
        s.Recorder.WriteResult("Pass");
    }
}
