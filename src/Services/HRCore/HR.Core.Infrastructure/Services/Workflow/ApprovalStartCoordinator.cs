using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>
/// PLAN-25 (G-02, decision D-1): <c>Recruitment:Approvals</c>. Applies to every approval started through
/// <see cref="ApprovalStartCoordinator"/> (recruitment and establishment changes).
/// </summary>
public sealed class RecruitmentApprovalOptions
{
    public const string SectionName = "Recruitment:Approvals";

    /// <summary>Start the default workflow when the policy engine did not start one (Off, Shadow, or Enforce without a trigger).</summary>
    public bool AlwaysStartWorkflow { get; set; } = true;

    /// <summary>
    /// Report a missing published definition as an error. When false the coordinator returns no instance and no error, so a
    /// caller with its own report (the repair dry run) decides; a submit never changes state without an instance either way.
    /// </summary>
    public bool FailIfDefinitionMissing { get; set; } = true;

    public static RecruitmentApprovalOptions From(IConfiguration configuration)
    {
        var options = new RecruitmentApprovalOptions();
        configuration.GetSection(SectionName).Bind(options);
        return options;
    }
}

/// <param name="PolicyRequest">Built only when the policy mode is not Off; the coordinator sets mode and actor.</param>
public sealed record ApprovalStartRequest(
    string EntityType,
    Guid EntityId,
    Guid? TenantId,
    string WorkflowCode,
    string PolicyDomain,
    Guid? ActorUserId,
    string? ActorUserName,
    string? ContextJson,
    Func<PolicyEnforceRequest>? PolicyRequest = null,
    string PolicyDeniedMessage = "مرفوض بسياسة الاعتماد.");

public sealed record ApprovalStartResult(Guid? InstanceId, string? Error)
{
    public bool Started => InstanceId is not null;
}

/// <summary>
/// PLAN-25 (G-02): the single place that decides how an approval starts. The policy engine may deny or start a different
/// workflow; it never removes the approval. Steps: (1) policy mode ≠ Off → enforce, a denial is returned; (2) an instance
/// started by the policy is used; (3) an in-progress instance of the same entity is reused (never two); (4) otherwise the
/// default workflow is started (<see cref="RecruitmentApprovalOptions.AlwaysStartWorkflow"/>); (5) no published definition →
/// <see cref="MissingDefinitionMessage"/>.
/// </summary>
public sealed class ApprovalStartCoordinator(
    HRCoreDbContext db,
    IPolicyOrchestrator policyOrchestrator,
    IWorkflowStarter workflowStarter,
    IConfiguration configuration)
{
    public const string MissingDefinitionMessage = "لا يوجد تعريف مسار اعتماد منشور لهذا الإجراء.";
    public const string StartDisabledMessage =
        "بدء مسار الاعتماد معطّل بالإعداد Recruitment:Approvals:AlwaysStartWorkflow، ولا تُقدَّم المعاملة دون مسار اعتماد.";

    public static PolicyEnforcementMode ResolveMode(IConfiguration configuration, string policyDomain)
    {
        var raw = configuration[$"PolicyEngine:{policyDomain}"]
            ?? configuration["PolicyEngine:DefaultMode"]
            ?? nameof(PolicyEnforcementMode.Off);
        return Enum.TryParse<PolicyEnforcementMode>(raw, true, out var mode) ? mode : PolicyEnforcementMode.Off;
    }

    public async Task<ApprovalStartResult> StartAsync(ApprovalStartRequest request, CancellationToken ct = default)
    {
        var options = RecruitmentApprovalOptions.From(configuration);
        var mode = ResolveMode(configuration, request.PolicyDomain);

        if (mode != PolicyEnforcementMode.Off && request.PolicyRequest is not null)
        {
            var policyRequest = request.PolicyRequest();
            policyRequest.Mode = mode;
            policyRequest.ActorUserId = request.ActorUserId;
            policyRequest.ActorUserName = request.ActorUserName;
            policyRequest.EntityId ??= request.EntityId;
            policyRequest.ContextJson ??= request.ContextJson;

            var policyResult = await policyOrchestrator.EnforceAsync(policyRequest, ct);
            if (!policyResult.Allowed)
                return new ApprovalStartResult(null, policyResult.Decision.DenyReason ?? request.PolicyDeniedMessage);
            if (policyResult.WorkflowInstanceId is Guid policyInstanceId)
                return new ApprovalStartResult(policyInstanceId, null);
        }

        var existing = await FindReusableInstanceAsync(request, ct);
        if (existing is not null)
            return new ApprovalStartResult(existing, null);

        if (!options.AlwaysStartWorkflow)
            return new ApprovalStartResult(null, StartDisabledMessage);

        var instanceId = await workflowStarter.StartByWorkflowCodeAsync(
            request.WorkflowCode,
            request.EntityType,
            request.EntityId,
            request.TenantId,
            request.ActorUserId,
            request.ActorUserName,
            request.ContextJson,
            request.PolicyDomain,
            ct);

        if (instanceId is null)
            return new ApprovalStartResult(null, options.FailIfDefinitionMissing ? MissingDefinitionMessage : null);

        return new ApprovalStartResult(instanceId, null);
    }

    /// <summary>
    /// Starts the approval and, only when an instance exists, runs <paramref name="applyAsync"/> (which sets the status and the
    /// instance id and saves) — inside one transaction on relational providers. Call it before mutating the entity: the policy
    /// ledger and the starter save the context themselves.
    /// </summary>
    public async Task<ApprovalStartResult> StartAndApplyAsync(
        ApprovalStartRequest request,
        Func<Guid, Task> applyAsync,
        CancellationToken ct = default)
    {
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            var result = await StartAsync(request, ct);
            if (result.InstanceId is Guid instanceId)
                await applyAsync(instanceId);

            // A refusal leaves only the policy ledger row in the transaction; it is kept.
            if (transaction is not null)
                await transaction.CommitAsync(ct);

            return result.Started || result.Error is not null
                ? result
                : result with { Error = MissingDefinitionMessage };
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// Never two instances: an in-progress instance of the same entity is reused only when it runs the requested workflow
    /// code and nobody has acted on it yet (first step, empty history) — so a pre-created or half-approved instance can never
    /// be adopted to skip a step or a stricter workflow (PLAN-25 review finding 1).
    /// </summary>
    private async Task<Guid?> FindReusableInstanceAsync(ApprovalStartRequest request, CancellationToken ct)
    {
        var candidates = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.EntityType == request.EntityType && i.EntityId == request.EntityId && i.Status == WorkflowStatus.InProgress)
            .OrderByDescending(i => i.StartedAt)
            .Select(i => new { i.Id, i.DefinitionId, i.CurrentStepLevel, i.StepHistoryJson })
            .ToListAsync(ct);
        foreach (var candidate in candidates.Where(c => c.CurrentStepLevel == 1 && (string.IsNullOrWhiteSpace(c.StepHistoryJson) || c.StepHistoryJson.Trim() == "[]")))
        {
            var code = await db.WorkflowDefinitions
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
                .AsNoTracking()
                .Where(d => d.Id == candidate.DefinitionId)
                .Select(d => d.Code)
                .FirstOrDefaultAsync(ct);
            if (string.Equals(code, request.WorkflowCode, StringComparison.OrdinalIgnoreCase))
                return candidate.Id;
        }

        return null;
    }
}
