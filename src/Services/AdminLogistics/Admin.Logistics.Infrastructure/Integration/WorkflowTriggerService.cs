using System.Net.Http.Json;
using System.Text.Json;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Admin.Logistics.Infrastructure.Integration;

public sealed class WorkflowTriggerService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<WorkflowTriggerService> logger)
{
    public const string HttpClientName = "HrCoreWorkflow";

    public async Task<Guid?> StartAssetAssignmentWorkflowAsync(
        Guid assignmentId,
        Guid assetId,
        Guid employeeId,
        string tagNumber,
        bool isPrivileged,
        decimal? value,
        CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        if (client.BaseAddress is null && string.IsNullOrWhiteSpace(configuration["Integration:HrCoreBaseUrl"]))
        {
            logger.LogWarning("HrCore workflow base URL not configured; skipping workflow start");
            return null;
        }

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

        try
        {
            var path = client.BaseAddress is not null
                ? "api/workflow-instances"
                : $"{configuration["Integration:HrCoreBaseUrl"]!.TrimEnd('/')}/api/workflow-instances";

            using var response = await client.PostAsJsonAsync(path, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Workflow start failed: {Status}", response.StatusCode);
                return null;
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (doc.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out var instanceId))
                return instanceId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Workflow start exception for assignment {AssignmentId}", assignmentId);
        }

        return null;
    }

    public async Task<Guid?> StartAssetLossWorkflowAsync(
        Guid lossReportId,
        Guid assetId,
        string tagNumber,
        bool isPrivileged,
        decimal? value,
        AssetLossReason reason,
        CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        if (client.BaseAddress is null && string.IsNullOrWhiteSpace(configuration["Integration:HrCoreBaseUrl"]))
        {
            logger.LogWarning("HrCore workflow base URL not configured; skipping loss workflow start");
            return null;
        }

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

        try
        {
            var path = client.BaseAddress is not null
                ? "api/workflow-instances"
                : $"{configuration["Integration:HrCoreBaseUrl"]!.TrimEnd('/')}/api/workflow-instances";

            using var response = await client.PostAsJsonAsync(path, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Loss workflow start failed: {Status}", response.StatusCode);
                return null;
            }

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (doc.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out var instanceId))
                return instanceId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Loss workflow start exception for report {ReportId}", lossReportId);
        }

        return null;
    }
}
