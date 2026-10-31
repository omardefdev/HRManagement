using System.Net.Http.Json;
using System.Text.Json;
using Admin.Logistics.Domain.Enums;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Admin.Logistics.Infrastructure.Integration;

/// <summary>
/// Starts HR.Core approval workflows for asset custody (<c>POST /api/workflow-instances</c>, scope hr-core.workflow).
/// PLAN-37: the client always has the HR.Core base address (AddHrCoreWorkflowClient refuses to start without it), and
/// every call carries the gateway caller proof, which the route requires outside Development. The full custody
/// approval loop is PLAN-45/46.
/// </summary>
public sealed class WorkflowTriggerService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<WorkflowTriggerService> logger)
{
    public const string HttpClientName = "HrCoreWorkflow";
    public const string WorkflowInstancesPath = "/api/workflow-instances";

    public Task<Guid?> StartAssetAssignmentWorkflowAsync(
        Guid assignmentId,
        Guid assetId,
        Guid employeeId,
        string tagNumber,
        bool isPrivileged,
        decimal? value,
        CancellationToken ct = default)
    {
        var context = JsonSerializer.Serialize(new
        {
            assetId,
            employeeId,
            tagNumber,
            isPrivileged,
            value
        });

        var payload = new
        {
            EntityType = "AssetAssignment",
            EntityId = assignmentId,
            ContextJson = context
        };

        return StartAsync(payload, "Workflow", "assignment", assignmentId, ct);
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
        var context = JsonSerializer.Serialize(new
        {
            assetId,
            lossReportId,
            tagNumber,
            isPrivileged,
            value,
            reason = reason.ToString()
        });

        var payload = new
        {
            EntityType = "AssetLossReport",
            EntityId = lossReportId,
            ContextJson = context
        };

        return StartAsync(payload, "Loss workflow", "report", lossReportId, ct);
    }

    private async Task<Guid?> StartAsync(object payload, string label, string entityLabel, Guid entityId, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, WorkflowInstancesPath.TrimStart('/'))
            {
                Content = JsonContent.Create(payload)
            };
            GatewayCallerProof.Sign(request, configuration, WorkflowInstancesPath);

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
}
