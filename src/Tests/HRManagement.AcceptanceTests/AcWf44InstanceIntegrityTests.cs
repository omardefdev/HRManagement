using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using static HRManagement.AcceptanceTests.RecruitmentAcceptance;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-WF-44 (PLAN-44) on the prod-like stack: no lost or doubled decision, one running instance per entity, outcomes that slipped are found and
/// repaired, and the inbox and the server bar the same makers. The anomalous rows of (c) and (e) are seeded with SQL, recorded in the evidence, and
/// removed at the end; the class also runs alone (group isolated), so no other class's repair run meets them.
/// The races that need a barrier to happen on every run are in <c>Wf044InstanceIntegrityPostgresTests</c>.
/// </summary>
[RequiredAcceptance]
public class AcWf44InstanceIntegrityTests
{
    private const string Ac = "AC-WF-44";
    private const string CreatorMessage = "لا يمكن للمنشئ اعتماد طلبه الخاص.";
    private const string ConflictMessage = "تغيّرت حالة الطلب أثناء الإجراء؛ أعد تحميل الصفحة وحاول مجدداً";
    private const string CoreMetricsUrl = "http://localhost:6210/metrics";

    private static string Requisition(string id) => $"/RecruitmentRequisitions/Details/{id}";

    private static async Task<HttpClient> ApiClientAsync(string persona, string correlationId) =>
        await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona(persona), correlationId);

    /// <summary>Takes a change off the table, so nothing later meets a pending request of this class.</summary>
    private static Task RemoveChangeAsync(Guid changeId) => Sql(
        "UPDATE \"WorkflowInstances\" SET \"Status\"='Cancelled', \"CompletedAt\"=now() " +
        $"WHERE \"EntityId\"='{changeId:D}' AND \"Status\"='InProgress'; " +
        $"UPDATE \"EstablishmentChangeRequests\" SET \"Status\"='Cancelled', \"WorkflowInstanceId\"=NULL WHERE \"Id\"='{changeId:D}';");

    // ---------------------------------------------------------------- a

    [SkippableFact]
    public async Task Ac44_a_two_submissions_at_once_leave_one_running_instance()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "44a", "AC-WF-44a central-a submits one establishment change twice at once", "central-a");
        s.Steps("""
            1. central-a imports a job catalog change and sends two submissions at once.
            2. Expected: one running instance for the change, and the change links it. Each response is a success or, when it read after the
               first had committed, the refusal of a request that is no longer a draft. Neither leaves a second instance.
            """);
        var correlation = Guid.NewGuid().ToString("D");
        using var central = await ApiClientAsync("central-a", correlation);
        using var again = await ApiClientAsync("central-a", correlation);
        var change = await ImportAsync(central, s.Recorder);
        try
        {
            var url = $"api/hr-core/api/establishment/changes/{change:D}/submit";
            var body = new { legalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004, decisionReference = "AC-WF-44a" };
            var responses = await Task.WhenAll(central.PostAsJsonAsync(url, body), again.PostAsJsonAsync(url, body));

            var statuses = responses.Select(r => (int)r.StatusCode).OrderBy(c => c).ToArray();
            Assert.Contains(statuses, c => c is >= 200 and < 300);
            Assert.All(statuses, c => Assert.True(c < 500, $"A submission answered {c}."));

            Assert.Equal(1, await Scalar($"SELECT count(*) FROM \"WorkflowInstances\" WHERE \"EntityId\"='{change:D}' AND \"Status\"='InProgress';"));
            Assert.Equal(1, await Scalar($"SELECT count(*) FROM \"WorkflowInstances\" WHERE \"EntityId\"='{change:D}';"));
            var linked = await Sql(
                "SELECT count(*) FROM \"EstablishmentChangeRequests\" r JOIN \"WorkflowInstances\" i ON i.\"Id\"=r.\"WorkflowInstanceId\" " +
                $"WHERE r.\"Id\"='{change:D}' AND r.\"Status\"='PendingApproval' AND i.\"Status\"='InProgress';");
            Assert.Equal("1", linked);

            s.Checks(new { change, statuses, runningInstances = 1 });
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            await RemoveChangeAsync(change);
        }
    }

    private static async Task<Guid> ImportAsync(HttpClient central, EvidenceRecorder recorder)
    {
        var csvPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "establishment", "job-catalog-sample.csv");
        await using var csv = File.OpenRead(csvPath);
        using var upload = new MultipartFormDataContent();
        var file = new StreamContent(csv);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        upload.Add(file, "file", "job-catalog-sample.csv");
        var response = await central.PostAsync("api/hr-core/api/establishment/job-catalog/import?mode=Standard", upload);
        var body = await response.Content.ReadAsStringAsync();
        recorder.CaptureHttp("import", "POST", "/api/hr-core/api/establishment/job-catalog/import", (int)response.StatusCode, body);
        Assert.True(response.IsSuccessStatusCode, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("changeRequestId").GetGuid();
    }

    // ---------------------------------------------------------------- b

    [SkippableFact]
    public async Task Ac44_b_two_approvals_at_once_give_one_decision_and_the_conflict_message()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "44b", "AC-WF-44b central-a2 approves step 1 twice at once", "central-a");
        s.Steps("""
            1. central-a submits a change. central-a2 sends two approvals of step 1 at once, each naming the step its page showed.
            2. Expected: one 200 and one 409 with the Arabic conflict message; one history entry and one signature.
            """);
        var correlation = Guid.NewGuid().ToString("D");
        using var central = await ApiClientAsync("central-a", correlation);
        var change = await EstablishmentChangeFlow.SubmitSampleChangeAsync(central, s.Recorder, "AC-WF-44b");
        try
        {
            var instance = await Sql($"SELECT \"WorkflowInstanceId\" FROM \"EstablishmentChangeRequests\" WHERE \"Id\"='{change:D}';");
            using var first = await ApiClientAsync("central-a2", correlation);
            using var second = await ApiClientAsync("central-a2", correlation);
            var url = $"api/hr-core/api/workflow-instances/{instance}/approve";
            var body = new { comments = "AC-WF-44b", targetStepLevel = 1 };

            var responses = await Task.WhenAll(first.PostAsJsonAsync(url, body), second.PostAsJsonAsync(url, body));

            var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
            for (var i = 0; i < responses.Length; i++)
                s.Recorder.CaptureHttp($"approve-{i}", "POST", "/" + url, (int)responses[i].StatusCode, bodies[i]);

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).OrderBy(c => (int)c));
            var loser = bodies[Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.Conflict)];
            Assert.Contains(ConflictMessage, loser, StringComparison.Ordinal);

            Assert.Equal("2|1", await Sql(
                "SELECT \"CurrentStepLevel\" || '|' || jsonb_array_length(\"StepHistoryJson\"::jsonb) " +
                $"FROM \"WorkflowInstances\" WHERE \"Id\"='{instance}';"));
            Assert.Equal(1, await Scalar(
                $"SELECT count(*) FROM \"SignatureRecords\" WHERE \"SubjectType\"='EstablishmentChangeRequest' AND \"SubjectId\"='{change:D}';"));

            s.Checks(new { change, instance, statuses = responses.Select(r => (int)r.StatusCode), historyEntries = 1, signatures = 1 });
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            await RemoveChangeAsync(change);
        }
    }

    // ---------------------------------------------------------------- c

    private static async Task<double?> UnappliedMetricAsync()
    {
        using var http = new HttpClient();
        var text = await http.GetStringAsync(CoreMetricsUrl);
        var line = text.Split('\n').FirstOrDefault(l =>
            l.StartsWith("workflow_outcome_unapplied", StringComparison.Ordinal) && l.Contains("RecruitmentRequisition", StringComparison.Ordinal));
        return line is null
            ? null
            : double.Parse(line[(line.LastIndexOf('}') + 1)..].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
    }

    private static async Task AssertUnappliedAsync(double expected)
    {
        double? last = null;
        for (var attempt = 0; attempt < 15; attempt++)
        {
            last = await UnappliedMetricAsync();
            if (last == expected)
                return;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.Fail($"workflow_outcome_unapplied_total: expected {expected}, last read {last?.ToString(CultureInfo.InvariantCulture) ?? "nothing"}.");
    }

    private static Task SeedFinishedInstanceAsync(string instanceId, string requisitionId) => Sql(
        "INSERT INTO \"WorkflowInstances\" (\"Id\",\"TenantId\",\"CreatedAt\",\"UpdatedAt\",\"DefinitionId\",\"DefinitionVersion\",\"EntityType\",\"EntityId\"," +
        "\"CurrentStepLevel\",\"Status\",\"StepHistoryJson\",\"InitiatedBy\",\"StartedAt\",\"CompletedAt\") " +
        $"SELECT '{instanceId}', '{AcceptanceSeedIds.AgencyAId}', now(), now(), d.\"Id\", d.\"Version\", 'RecruitmentRequisition', '{requisitionId}', 2, 'Completed', '[]', NULL, now(), now() " +
        "FROM \"WorkflowDefinitions\" d WHERE d.\"Code\"='RECRUITMENT-REQUISITION-APPROVAL' ORDER BY d.\"Version\" DESC LIMIT 1;");

    private static JsonElement ItemOf(JsonDocument report, string instanceId) =>
        report.RootElement.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("action").GetString() == "ApplyFinishedOutcome"
                && string.Equals(i.GetProperty("workflowInstanceId").GetString(), instanceId, StringComparison.OrdinalIgnoreCase));

    private static JsonDocument LatestReport()
    {
        var folder = Path.Combine(AcceptanceHarness.EvidenceRoot, "AC-HCM-25", "repair");
        var latest = new DirectoryInfo(folder).GetFiles("repair-*.json").OrderByDescending(f => f.LastWriteTimeUtc).First();
        return JsonDocument.Parse(File.ReadAllText(latest.FullName).TrimStart('﻿'));
    }

    [SkippableFact]
    public async Task Ac44_c_an_outcome_that_slipped_is_listed_repaired_and_the_metric_returns_to_zero()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "44c", "AC-WF-44c a completed instance with its requisition still pending", "central-a");
        s.Steps("""
            Seeded with SQL: a completed instance linked to a requisition still PendingApproval, as a build before this plan could leave it.
            1. The operator's dry run lists it and changes nothing; the metric counts it.
            2. Applying makes the requisition Approved, audits the operator, leaves the instance as it was, and the metric returns to 0.
            Negative: a completed instance the requisition does not link is listed «غير مرتبطة» and never applied.
            """);

        var reqId = await CreateRequisitionAsync(s, "44c");
        var slipped = Guid.NewGuid().ToString("D");
        await SeedFinishedInstanceAsync(slipped, reqId);
        await Sql($"UPDATE \"RecruitmentRequisitions\" SET \"Status\"='PendingApproval', \"WorkflowInstanceId\"='{slipped}' WHERE \"Id\"='{reqId}';");
        s.Recorder.WriteBefore(new { seeded = "completed instance, pending linked requisition", requisitionId = reqId, instanceId = slipped });

        await AcHcm25ProductionApprovalTests.RunRepairAsync(apply: false);
        using (var dry = LatestReport())
            Assert.Equal("Listed", ItemOf(dry, slipped).GetProperty("result").GetString());
        Assert.StartsWith("PendingApproval|", await RequisitionStateAsync(reqId), StringComparison.Ordinal);
        await AssertUnappliedAsync(1);

        await AcHcm25ProductionApprovalTests.RunRepairAsync(apply: true);
        using (var applied = LatestReport())
            Assert.Equal("Repaired", ItemOf(applied, slipped).GetProperty("result").GetString());
        Assert.Equal("Approved", await Sql($"SELECT \"Status\" FROM \"RecruitmentRequisitions\" WHERE \"Id\"='{reqId}';"));
        Assert.Equal("Completed|[]", await Sql($"SELECT \"Status\" || '|' || \"StepHistoryJson\" FROM \"WorkflowInstances\" WHERE \"Id\"='{slipped}';"));
        Assert.Equal(1, await Scalar(
            $"SELECT count(*) FROM \"AuditLogs\" WHERE \"EntityId\"='{reqId}' AND \"Action\"='WorkflowOutcomeReapplied' AND \"UserId\" IS NOT NULL;"));
        await AssertUnappliedAsync(0);

        // The negative: a running link, and a completed instance beside it that nothing links.
        var other = await CreateRequisitionAsync(s, "44c-negative");
        await s["central-a"].PostAsync("44c-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{other}", ReqDetails(other), []);
        var stray = Guid.NewGuid().ToString("D");
        await SeedFinishedInstanceAsync(stray, other);
        try
        {
            await AcHcm25ProductionApprovalTests.RunRepairAsync(apply: true);
            using var report = LatestReport();
            var item = ItemOf(report, stray);
            Assert.Equal("NotLinked", item.GetProperty("result").GetString());
            Assert.Equal("غير مرتبطة", item.GetProperty("error").GetString());
            Assert.StartsWith("PendingApproval|", await RequisitionStateAsync(other), StringComparison.Ordinal);
            await AssertUnappliedAsync(0);

            s.Checks(new { reqId, slipped, other, stray, metricAfterRepair = 0 });
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            await Sql($"DELETE FROM \"WorkflowInstances\" WHERE \"Id\"='{stray}';");
        }
    }

    // ---------------------------------------------------------------- d

    /// <summary>A pending legacy request of its own, copied from the seeded one so the scenario can finish it.</summary>
    private static async Task<string> LegacyRequestAsync()
    {
        var seeded = RecruitmentStates.Load().LegacyApprovalRequest.Id;
        var id = Guid.NewGuid().ToString("D");
        await Sql(
            $"CREATE TEMP TABLE r44 AS SELECT * FROM \"ApprovalRequests\" WHERE \"Id\"='{seeded:D}'; " +
            $"UPDATE r44 SET \"Id\"='{id}'; INSERT INTO \"ApprovalRequests\" SELECT * FROM r44; " +
            $"CREATE TEMP TABLE s44 AS SELECT * FROM \"ApprovalSteps\" WHERE \"ApprovalRequestId\"='{seeded:D}'; " +
            $"UPDATE s44 SET \"Id\"=gen_random_uuid(), \"ApprovalRequestId\"='{id}'; INSERT INTO \"ApprovalSteps\" SELECT * FROM s44;");
        return id;
    }

    private static Task<string> LegacyStatusAsync(string id) => Sql($"SELECT \"Status\" FROM \"ApprovalRequests\" WHERE \"Id\"='{id}';");

    [SkippableFact]
    public async Task Ac44_d_the_legacy_path_asks_for_the_steps_role_and_only_the_requester_cancels()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "44d", "AC-WF-44d legacy ApprovedCV: step role and requester-only cancel", "central-a", "central-a2", "a-management-approver");
        s.Steps("""
            A legacy ApprovedCV request by central-a whose step needs hr-core.
            1. Negative: a-management-approver approves through Approvals/Approve: refused by the step's role, nothing changes.
            2. Negative: central-a2 (hr-core, not the requester) cancels: refused, nothing changes.
            3. Positive: central-a2 approves, and the requester cancels another request.
            """);
        var id = await LegacyRequestAsync();
        var second = await LegacyRequestAsync();
        var before = await LegacyStatusAsync(id);
        s.Recorder.WriteBefore(new { id, second, status = before });
        var review = $"ui/hr-core/Approvals/Review/{id}";
        try
        {
            var refused = await s["a-management-approver"].PostAsync("44d-approve-no-role", $"ui/hr-core/Approvals/Approve/{id}", review, []);
            Assert.Contains("هذه الخطوة تتطلب دور «hr-core».", refused, StringComparison.Ordinal);
            Assert.Equal(before, await LegacyStatusAsync(id));

            var cancel = await s["central-a2"].PostAsync("44d-cancel-not-requester", $"ui/hr-core/Approvals/Cancel/{id}", review, []);
            Assert.Contains("لا يلغي الطلب إلا صاحبه", cancel, StringComparison.Ordinal);
            Assert.Equal(before, await LegacyStatusAsync(id));

            await s["central-a2"].PostAsync("44d-approve", $"ui/hr-core/Approvals/Approve/{id}", review, []);
            Assert.NotEqual(before, await LegacyStatusAsync(id));

            await s["central-a"].PostAsync("44d-cancel-requester", $"ui/hr-core/Approvals/Cancel/{second}", $"ui/hr-core/Approvals/Review/{second}", []);
            Assert.NotEqual(before, await LegacyStatusAsync(second));

            s.Checks(new { id, second, approved = await LegacyStatusAsync(id), cancelled = await LegacyStatusAsync(second) });
            s.Recorder.WriteResult("Pass");
        }
        finally
        {
            await Sql(
                $"DELETE FROM \"ApprovalSteps\" WHERE \"ApprovalRequestId\" IN ('{id}','{second}'); " +
                $"DELETE FROM \"ApprovalRequests\" WHERE \"Id\" IN ('{id}','{second}');");
        }
    }

    // ---------------------------------------------------------------- e

    [SkippableFact]
    public async Task Ac44_e_inbox_and_server_bar_the_submitter_of_a_requisition_with_no_creator()
    {
        AcceptanceEnv.RequireEnabled();
        var s = await StartAsync(Ac, "44e", "AC-WF-44e a requisition with an empty CreatedBy: the submitter is barred in both places", "central-a", "br1-mgr");
        s.Steps("""
            central-a submits a requisition; its CreatedBy is then emptied with SQL, as an old row can be.
            1. The inbox does not list it for central-a. Control: br1-mgr, with the same step role, does see it.
            2. Negative: central-a POSTs the approval of step 1: refused with the creator message, nothing changes.
            """);
        var id = await CreateRequisitionAsync(s, "44e");
        await s["central-a"].PostAsync("44e-submit", $"ui/hr-core/RecruitmentRequisitions/Submit/{id}", ReqDetails(id), []);
        await Sql($"UPDATE \"RecruitmentRequisitions\" SET \"CreatedBy\"=NULL WHERE \"Id\"='{id}';");
        var before = await RequisitionStateAsync(id);
        s.Recorder.WriteBefore(new { seeded = "CreatedBy emptied", id, state = before });

        Assert.False((await InboxAsync(s["central-a"], "44e-inbox-submitter")).Has(Requisition(id)), "The submitter sees an item it cannot act on.");
        Assert.True((await InboxAsync(s["br1-mgr"], "44e-inbox-control")).Has(Requisition(id)), "The control persona should see the item.");

        var refused = await s["central-a"].PostAsync("44e-post-approve", $"ui/hr-core/RecruitmentRequisitions/ApproveWorkflow/{id}", ReqDetails(id),
            [new("RequisitionId", id), new("Comments", "AC-WF-44e"), new("TargetStepLevel", "1")]);
        Assert.Contains(CreatorMessage, refused, StringComparison.Ordinal);
        Assert.Equal(before, await RequisitionStateAsync(id));

        await s["br1-mgr"].PostAsync("44e-cleanup-reject", $"ui/hr-core/RecruitmentRequisitions/RejectWorkflow/{id}", ReqDetails(id),
            [new("RequisitionId", id), new("Comments", "AC-WF-44e clean-up")]);
        s.Checks(new { id, before, refusal = CreatorMessage });
        s.Recorder.WriteResult("Pass");
    }
}
