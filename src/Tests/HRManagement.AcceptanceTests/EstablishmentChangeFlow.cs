using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-25: a submitted establishment change applies only after hr-core (not its creator) and the management approver. PLAN-41:
/// the AC-EST classes written before that rule go through this flow.
/// </summary>
internal static class EstablishmentChangeFlow
{
    public static Task<string> StatusAsync(Guid changeRequestId) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db",
            $"SELECT \"Status\" FROM \"EstablishmentChangeRequests\" WHERE \"Id\"='{changeRequestId:D}';");

    public static async Task ApproveBothStepsAsync(Guid changeRequestId, string correlationId, EvidenceRecorder recorder)
    {
        Assert.Equal("PendingApproval", (await StatusAsync(changeRequestId)).Trim());
        var instanceId = Guid.Parse((await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("hr_core_db",
            $"SELECT \"WorkflowInstanceId\" FROM \"EstablishmentChangeRequests\" WHERE \"Id\"='{changeRequestId:D}';")).Trim());

        foreach (var (personaId, level) in new[] { ("central-a2", 1), ("a-management-approver", 2) })
        {
            using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(AcceptanceHarness.RequirePersona(personaId), correlationId);
            var url = $"api/hr-core/api/workflow-instances/{instanceId:D}/approve";
            var response = await http.PostAsJsonAsync(url, new { comments = $"step {level}", targetStepLevel = level });
            var body = await response.Content.ReadAsStringAsync();
            recorder.CaptureHttp($"approve-step-{level}-{personaId}", "POST", $"/{url}", (int)response.StatusCode, body);
            Assert.True(response.IsSuccessStatusCode, body);
        }

        // PLAN-44: a change that cannot apply refuses the last approval, so this is reached only by a change that applied.
        Assert.Equal("Applied", (await StatusAsync(changeRequestId)).Trim());
    }

    /// <summary>Imports the sample job catalog and submits it; the change stays pending for the scenario to look at.</summary>
    public static async Task<Guid> SubmitSampleChangeAsync(HttpClient central, EvidenceRecorder recorder, string decisionReference)
    {
        var csvPath = Path.Combine(AcceptanceHarness.RepoRoot, "testdata", "acceptance", "establishment", "job-catalog-sample.csv");
        await using var csv = File.OpenRead(csvPath);
        using var upload = new MultipartFormDataContent();
        var file = new StreamContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        upload.Add(file, "file", "job-catalog-sample.csv");

        var importResponse = await central.PostAsync("api/hr-core/api/establishment/job-catalog/import?mode=Standard", upload);
        var importBody = await importResponse.Content.ReadAsStringAsync();
        Assert.True(importResponse.IsSuccessStatusCode, importBody);
        using var document = JsonDocument.Parse(importBody);
        var id = document.RootElement.GetProperty("changeRequestId").GetGuid();

        var submitUrl = $"api/hr-core/api/establishment/changes/{id:D}/submit";
        var submit = await central.PostAsJsonAsync(submitUrl, new { legalSourceId = Syr001LegislationIds.Law50_2004, decisionReference });
        var submitBody = await submit.Content.ReadAsStringAsync();
        recorder.CaptureHttp("submit-own-change", "POST", $"/{submitUrl}", (int)submit.StatusCode, submitBody);
        Assert.True(submit.IsSuccessStatusCode, submitBody);
        return id;
    }
}
