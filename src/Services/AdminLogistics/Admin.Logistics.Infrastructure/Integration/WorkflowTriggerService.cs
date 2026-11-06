using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Admin.Logistics.Domain.Enums;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using Microsoft.Extensions.Logging;

namespace Admin.Logistics.Infrastructure.Integration;

/// <summary>
/// Starts HR.Core approval workflows for asset custody (<c>POST /api/workflow-instances</c>, scope hr-core.workflow).
/// PLAN-37: the client always has the HR.Core base address (AddHrCoreWorkflowClient refuses to start without it). PLAN-42: the
/// service token is enough; no service makes a gateway proof. PLAN-43: the client forwards the acting user, who is the requester
/// HR.Core records; the body never names the requester. The full custody approval loop is PLAN-45/46.
/// </summary>
public sealed partial class WorkflowTriggerService(
    IHttpClientFactory httpClientFactory,
    ILogger<WorkflowTriggerService> logger)
{
    public const string HttpClientName = "HrCoreWorkflow";
    public const string WorkflowInstancesPath = "/api/workflow-instances";
    public const string AssignmentEntityType = "AssetAssignment";
    public const string LossEntityType = "AssetLossReport";
    public const string AssignmentWorkflowCode = "ASSET-ASSIGN-APPROVAL";
    public const string LossWorkflowCode = "ASSET-LOSS-APPROVAL";

    public Task<Guid?> StartAssetAssignmentWorkflowAsync(
        Guid assignmentId,
        Guid assetId,
        Guid employeeId,
        string tagNumber,
        bool isPrivileged,
        decimal? value,
        CancellationToken ct = default)
    {
        var request = new WorkflowApprovalStartRequest(
            AssignmentEntityType,
            assignmentId,
            AssignmentWorkflowCode,
            $"{AssignmentEntityType}:{assignmentId:D}",
            [],
            null,
            Title("تسليم عهدة", tagNumber),
            DetailsUrl(assetId),
            Facts(assetId, tagNumber, isPrivileged, value, ("employeeId", employeeId.ToString("D"))),
            employeeId);

        return StartAsync(request, "Workflow", "assignment", assignmentId, ct);
    }

    public Task<Guid?> StartAssetLossWorkflowAsync(
        Guid lossReportId,
        Guid assetId,
        string tagNumber,
        bool isPrivileged,
        decimal? value,
        AssetLossReason reason,
        CancellationToken ct = default)
    {
        var request = new WorkflowApprovalStartRequest(
            LossEntityType,
            lossReportId,
            LossWorkflowCode,
            $"{LossEntityType}:{lossReportId:D}",
            [],
            null,
            Title("فقدان أصل", tagNumber),
            DetailsUrl(assetId),
            Facts(assetId, tagNumber, isPrivileged, value, ("lossReportId", lossReportId.ToString("D")), ("reason", reason.ToString())));

        return StartAsync(request, "Loss workflow", "report", lossReportId, ct);
    }

    private static string DetailsUrl(Guid assetId) => $"/ui/admin-logistics/Assets/Details/{assetId:D}";

    /// <summary>HR.Core refuses a title with a long number, so a tag of that shape stays out of it.</summary>
    private static string Title(string label, string tagNumber) =>
        string.IsNullOrWhiteSpace(tagNumber) || LongNumber().IsMatch(tagNumber) ? label : $"{label} {tagNumber.Trim()}";

    private static Dictionary<string, string> Facts(
        Guid assetId, string tagNumber, bool isPrivileged, decimal? value, params (string Key, string Value)[] extra)
    {
        var facts = new Dictionary<string, string>
        {
            ["assetId"] = assetId.ToString("D"),
            ["tagNumber"] = tagNumber,
            ["isPrivileged"] = isPrivileged ? "true" : "false"
        };
        if (value is { } amount)
            facts["value"] = amount.ToString(CultureInfo.InvariantCulture);
        foreach (var (key, factValue) in extra)
            facts[key] = factValue;
        return facts;
    }

    private async Task<Guid?> StartAsync(WorkflowApprovalStartRequest body, string label, string entityLabel, Guid entityId, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, WorkflowInstancesPath.TrimStart('/'))
            {
                Content = JsonContent.Create(body)
            };

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("{Label} start failed: {Status}", label, response.StatusCode);
                return null;
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (doc.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out var instanceId))
                return instanceId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Label} start exception for {EntityLabel} {EntityId}", label, entityLabel, entityId);
        }

        return null;
    }

    [GeneratedRegex(@"\d{8,}")]
    private static partial Regex LongNumber();
}
