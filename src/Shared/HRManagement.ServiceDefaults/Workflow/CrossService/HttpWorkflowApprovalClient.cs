using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Workflow.CrossService;

/// <summary>
/// PLAN-46 (G-WF-01): the three calls of the contract, over the owner's service token, with the acting user and the tenant. Status codes map
/// as the plan says: 400 Invalid, 401/403 Forbidden, 409 Conflict, 422 DefinitionMissing, everything else and every exception
/// EngineUnavailable. Only the caller's own cancellation throws.
/// </summary>
public sealed class HttpWorkflowApprovalClient(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<HttpWorkflowApprovalClient> logger) : IWorkflowApprovalClient
{
    public const string HttpClientName = "hr-core-workflow";
    public const string WorkflowInstancesPath = "api/workflow-instances";

    public async Task<WorkflowApprovalStartResult> StartAsync(WorkflowApprovalStartRequest request, CancellationToken ct)
    {
        if (!TryPrepare(out var client, out var tenant, out var errorCode, out var error))
        {
            logger.LogError("Approval start for {EntityId} refused here: {ErrorCode}.", request.EntityId, errorCode);
            return Failed(errorCode, error);
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, WorkflowInstancesPath) { Content = JsonContent.Create(request) };
            AddTenant(message, tenant);

            using var response = await client.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                var (body, _) = await ReadErrorAsync(response, ct);
                var code = CodeFor(response.StatusCode);
                logger.LogWarning("Approval start for {EntityId} failed: {Status}.", request.EntityId, response.StatusCode);
                return Failed(code, body ?? WorkflowApprovalErrorCodes.MessageFor(code));
            }

            var started = await response.Content.ReadFromJsonAsync<WorkflowApprovalStartResponse>(cancellationToken: ct);
            return started is null || started.Id == Guid.Empty
                ? Failed(WorkflowApprovalErrorCodes.EngineUnavailable, WorkflowApprovalErrorCodes.EngineUnavailableMessage)
                : new WorkflowApprovalStartResult(true, started.Id, started.AlreadyActive, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Approval start for {EntityId} failed.", request.EntityId);
            return Failed(WorkflowApprovalErrorCodes.EngineUnavailable, WorkflowApprovalErrorCodes.EngineUnavailableMessage);
        }
    }

    public async Task<WorkflowApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct)
    {
        if (!TryPrepare(out var client, out var tenant, out var errorCode, out var error))
        {
            logger.LogError("Withdrawing instance {InstanceId} refused here: {ErrorCode}.", instanceId, errorCode);
            return new WorkflowApprovalCancelResult(false, false, null, errorCode, error);
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{WorkflowInstancesPath}/{instanceId:D}/cancel")
            {
                Content = JsonContent.Create(new WorkflowApprovalCancelRequest(reason))
            };
            AddTenant(message, tenant);

            using var response = await client.SendAsync(message, ct);
            if (response.IsSuccessStatusCode)
                return new WorkflowApprovalCancelResult(true, false, null, null, null);

            var (body, engineStatus) = await ReadErrorAsync(response, ct);

            // A 409 that names a finished status lost to the outcome; one that names none lost to a decision being written.
            if (response.StatusCode == HttpStatusCode.Conflict && WorkflowApprovalOutcomes.FromEngineStatus(engineStatus) is { } finalOutcome)
                return new WorkflowApprovalCancelResult(false, true, finalOutcome, null, body);

            var code = CodeFor(response.StatusCode);
            logger.LogWarning("Withdrawing instance {InstanceId} failed: {Status}.", instanceId, response.StatusCode);
            return new WorkflowApprovalCancelResult(false, false, null, code, body ?? WorkflowApprovalErrorCodes.MessageFor(code));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Withdrawing instance {InstanceId} failed.", instanceId);
            return new WorkflowApprovalCancelResult(
                false, false, null, WorkflowApprovalErrorCodes.EngineUnavailable, WorkflowApprovalErrorCodes.EngineUnavailableMessage);
        }
    }

    public async Task<WorkflowApprovalStatusDto?> GetStatusAsync(Guid instanceId, CancellationToken ct)
    {
        if (!TryPrepare(out var client, out var tenant, out var errorCode, out _))
        {
            logger.LogError("Status of instance {InstanceId} refused here: {ErrorCode}.", instanceId, errorCode);
            return null;
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"{WorkflowInstancesPath}/{instanceId:D}/external-status");
            AddTenant(message, tenant);

            using var response = await client.SendAsync(message, ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<WorkflowApprovalStatusDto>(cancellationToken: ct);

            logger.LogWarning("Status of instance {InstanceId} failed: {Status}.", instanceId, response.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Status of instance {InstanceId} failed.", instanceId);
            return null;
        }
    }

    /// <summary>No address is never a success, and a call with no tenant would land in the national tenant (PLAN-45 section 8).</summary>
    private bool TryPrepare(out HttpClient client, out Guid tenant, out string errorCode, out string error)
    {
        client = httpClientFactory.CreateClient(HttpClientName);
        tenant = Guid.Empty;
        errorCode = string.Empty;
        error = string.Empty;

        if (client.BaseAddress is null)
        {
            (errorCode, error) = (WorkflowApprovalErrorCodes.EngineUnavailable, WorkflowApprovalErrorCodes.EngineUnavailableMessage);
            return false;
        }

        // The owner puts the record's tenant in the flow; the request's own tenant is only the fallback.
        var current = TenantIdFlow.Get() ?? httpContextAccessor.HttpContext?.Items[TenantIdMiddleware.ItemKey] as Guid?;
        if (current is not Guid resolved || resolved == Guid.Empty)
        {
            (errorCode, error) = (WorkflowApprovalErrorCodes.Invalid, WorkflowApprovalErrorCodes.NoTenantMessage);
            return false;
        }

        tenant = resolved;
        return true;
    }

    private static void AddTenant(HttpRequestMessage message, Guid tenant) =>
        message.Headers.TryAddWithoutValidation(TenantIdMiddleware.HeaderName, tenant.ToString("D"));

    private static WorkflowApprovalStartResult Failed(string code, string? error) => new(false, null, false, code, error);

    private static string CodeFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => WorkflowApprovalErrorCodes.Invalid,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => WorkflowApprovalErrorCodes.Forbidden,
        HttpStatusCode.Conflict => WorkflowApprovalErrorCodes.Conflict,
        HttpStatusCode.UnprocessableEntity => WorkflowApprovalErrorCodes.DefinitionMissing,
        _ => WorkflowApprovalErrorCodes.EngineUnavailable
    };

    private static async Task<(string? Message, string? Status)> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return (Text(doc.RootElement, "error"), Text(doc.RootElement, "status"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
