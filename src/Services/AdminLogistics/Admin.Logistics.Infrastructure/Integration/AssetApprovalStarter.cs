using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Admin.Logistics.Infrastructure.Integration;

/// <summary>What HR.Core answered: the instance and its state, or why there is none. A retry for a committed start finds the same instance.</summary>
public sealed record AssetApprovalStartResult(bool Success, Guid? InstanceId, string? Error, string? InstanceStatus = null);

public enum AssetApprovalCancelOutcome
{
    Cancelled,

    /// <summary>The instance finished first: <see cref="AssetApprovalCancelResult.InstanceStatus"/> says how.</summary>
    Finished,

    /// <summary>A decision was being written at the same moment.</summary>
    Conflict,

    Failed
}

public sealed record AssetApprovalCancelResult(AssetApprovalCancelOutcome Outcome, string? InstanceStatus = null, string? Error = null);

public interface IAssetApprovalStarter
{
    Task<AssetApprovalStartResult> StartAsync(AssetAssignment assignment, Asset asset, CancellationToken ct = default);

    Task<AssetApprovalStartResult> StartAsync(AssetLossReport report, Asset asset, Guid? holderEmployeeId, CancellationToken ct = default);

    Task<AssetApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct = default);
}

/// <summary>
/// PLAN-45 (G-WF-02): the trusted custody start (<c>POST /api/workflow-instances</c>, scope hr-core.workflow), and its withdrawal. The key
/// is stable across retries, so HR.Core answers a retry with the instance it already made. The requester is the person who made the
/// request, whoever retries it, and is excluded from every step. Nothing here throws: a failure is an answer. PLAN-37: the client always
/// has the HR.Core base address. PLAN-42: the service token is enough.
/// </summary>
public sealed partial class AssetApprovalStarter(
    IHttpClientFactory httpClientFactory,
    AdminLogisticsDbContext db,
    ILogger<AssetApprovalStarter> logger) : IAssetApprovalStarter
{
    public const string HttpClientName = "HrCoreWorkflow";
    public const string WorkflowInstancesPath = "/api/workflow-instances";
    public const string AssignmentEntityType = "AssetAssignment";
    public const string LossEntityType = "AssetLossReport";
    public const string AssignmentWorkflowCode = "ASSET-ASSIGN-APPROVAL";
    public const string LossWorkflowCode = "ASSET-LOSS-APPROVAL";

    public const string UnavailableMessage = "تعذّر الوصول إلى خدمة الاعتماد.";

    public async Task<AssetApprovalStartResult> StartAsync(AssetAssignment assignment, Asset asset, CancellationToken ct = default)
    {
        try
        {
            var request = new WorkflowApprovalStartRequest(
                AssignmentEntityType,
                assignment.Id,
                AssignmentWorkflowCode,
                IdempotencyKey(AssignmentEntityType, assignment.Id),
                Excluded(assignment),
                await OrgUnitOfAsync(assignment.EmployeeId, ct),
                Title("تسليم عهدة", asset.TagNumber),
                DetailsUrl(asset.Id),
                Facts(asset, ("employeeId", assignment.EmployeeId.ToString("D"))),
                assignment.EmployeeId);

            return await PostStartAsync(request, assignment, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Failed(ex, assignment.Id);
        }
    }

    public async Task<AssetApprovalStartResult> StartAsync(
        AssetLossReport report, Asset asset, Guid? holderEmployeeId, CancellationToken ct = default)
    {
        try
        {
            var request = new WorkflowApprovalStartRequest(
                LossEntityType,
                report.Id,
                LossWorkflowCode,
                IdempotencyKey(LossEntityType, report.Id),
                Excluded(report),
                holderEmployeeId is Guid holder ? await OrgUnitOfAsync(holder, ct) : null,
                Title("فقدان أصل", asset.TagNumber),
                DetailsUrl(asset.Id),
                Facts(asset, ("lossReportId", report.Id.ToString("D")), ("reason", report.Reason.ToString())),
                holderEmployeeId);

            return await PostStartAsync(request, report, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Failed(ex, report.Id);
        }
    }

    public async Task<AssetApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{WorkflowInstancesPath.TrimStart('/')}/{instanceId:D}/cancel")
            {
                Content = JsonContent.Create(new WorkflowApprovalCancelRequest(reason))
            };

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return new AssetApprovalCancelResult(AssetApprovalCancelOutcome.Cancelled);

            var error = await ReadErrorAsync(response, ct);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return error.Status is null
                    ? new AssetApprovalCancelResult(AssetApprovalCancelOutcome.Conflict, Error: error.Message)
                    : new AssetApprovalCancelResult(AssetApprovalCancelOutcome.Finished, error.Status, error.Message);
            }

            logger.LogWarning("Withdrawing instance {InstanceId} failed: {Status}", instanceId, response.StatusCode);
            return new AssetApprovalCancelResult(AssetApprovalCancelOutcome.Failed, Error: error.Message ?? UnavailableMessage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Withdrawing instance {InstanceId} failed", instanceId);
            return new AssetApprovalCancelResult(AssetApprovalCancelOutcome.Failed, Error: UnavailableMessage);
        }
    }

    private async Task<AssetApprovalStartResult> PostStartAsync(WorkflowApprovalStartRequest body, IAssetApprovalRequest requester, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, WorkflowInstancesPath.TrimStart('/'))
        {
            Content = JsonContent.Create(body)
        };

        // The handler keeps a header already there: a retry by a colleague does not make the colleague the requester.
        if (requester.CreatedBy is Guid requesterId)
        {
            request.Headers.TryAddWithoutValidation(ForwardActingUserHandler.HeaderName, requesterId.ToString("D"));
            if (!string.IsNullOrWhiteSpace(requester.RequestedByName))
                request.Headers.TryAddWithoutValidation(ForwardActingUserHandler.NameHeaderName, Uri.EscapeDataString(requester.RequestedByName));
        }

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, ct);
            logger.LogWarning("Approval start for {EntityType} {EntityId} failed: {Status}", body.EntityType, body.EntityId, response.StatusCode);
            return new AssetApprovalStartResult(false, null, error.Message ?? UnavailableMessage);
        }

        var started = await response.Content.ReadFromJsonAsync<WorkflowApprovalStartResponse>(cancellationToken: ct);
        return started is null || started.Id == Guid.Empty
            ? new AssetApprovalStartResult(false, null, UnavailableMessage)
            : new AssetApprovalStartResult(true, started.Id, null, started.Status);
    }

    private AssetApprovalStartResult Failed(Exception ex, Guid entityId)
    {
        logger.LogWarning(ex, "Approval start for {EntityId} failed", entityId);
        return new AssetApprovalStartResult(false, null, UnavailableMessage);
    }

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

    private async Task<Guid?> OrgUnitOfAsync(Guid employeeId, CancellationToken ct) =>
        await db.EmployeeOrgUnitSnapshots.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => e.PrimaryOrgUnitId)
            .FirstOrDefaultAsync(ct);

    private static Guid[] Excluded(IAssetApprovalRequest request) =>
        request.CreatedBy is Guid requester ? [requester] : [];

    private static string IdempotencyKey(string entityType, Guid id) => $"{entityType}:{id:D}";

    private static string DetailsUrl(Guid assetId) => $"/ui/admin-logistics/Assets/Details/{assetId:D}";

    /// <summary>HR.Core refuses a title with a long number, so a tag of that shape stays out of it.</summary>
    private static string Title(string label, string? tagNumber) =>
        string.IsNullOrWhiteSpace(tagNumber) || LongNumber().IsMatch(tagNumber) ? label : $"{label} {tagNumber.Trim()}";

    private static Dictionary<string, string> Facts(Asset asset, params (string Key, string Value)[] extra)
    {
        var facts = new Dictionary<string, string>
        {
            ["assetId"] = asset.Id.ToString("D"),
            ["tagNumber"] = asset.TagNumber,
            ["isPrivileged"] = asset.IsPrivileged ? "true" : "false"
        };
        if (asset.Value is { } amount)
            facts["value"] = amount.ToString(CultureInfo.InvariantCulture);
        foreach (var (key, factValue) in extra)
            facts[key] = factValue;
        return facts;
    }

    [GeneratedRegex(@"\d{8,}")]
    private static partial Regex LongNumber();
}
