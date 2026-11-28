using System.Security.Claims;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Web.ViewModels;

/// <summary>
/// PLAN-60 (P-11): what the separation pages show of a record — its status, its approval instance (link and state), its decision, its
/// clearance items by mode, who created it (or its source), and the actions its status allows the signed-in person.
/// </summary>
public sealed class SeparationPanelModel
{
    public required SeparationRecord Record { get; init; }

    public required IReadOnlyList<SeparationChecklist> Items { get; init; }

    public string? EmployeeName { get; init; }

    public AdministrativeDecision? Decision { get; init; }

    public WorkflowApprovalStatusDto? Approval { get; init; }

    /// <summary>The record has an instance, and the engine did not answer about it.</summary>
    public bool ApprovalUnavailable { get; init; }

    public bool CanWrite { get; init; }

    public bool CanApprove { get; init; }

    public bool IsCreator { get; init; }

    public DateOnly TodayInDamascus { get; init; }

    public SeparationStatus Status => Record.Status;

    public bool CanSubmit => CanWrite && Status == SeparationStatus.Draft;

    public bool CanWithdraw => CanWrite && Status == SeparationStatus.PendingApproval;

    public bool CanReopen => CanWrite && Status == SeparationStatus.Rejected;

    public bool CanIssueDecision => CanApprove && Status == SeparationStatus.Approved && Record.DecisionId is null;

    public bool CanExecute => CanApprove && Status == SeparationStatus.Approved;

    public bool CanCancel => CanApprove && SeparationWorkflowRules.IsCancellable(Status);

    public bool ItemsEditable => Status is SeparationStatus.Draft or SeparationStatus.PendingApproval or SeparationStatus.Approved;

    public bool HasOpenExceptionRequest => Items.Any(i => i.ExceptionRequestedBy is not null && !i.IsExcepted && !i.IsCompleted);
}

/// <summary>Builds <see cref="SeparationPanelModel"/> for the record pages and the wizard alike.</summary>
public sealed class SeparationPanelBuilder(
    ContractsDbContext db,
    IWorkflowApprovalClient approvals,
    IHrCoreLookupClient hrCoreLookup,
    IAuthorizationService authorization,
    HR.Contracts.Application.Features.SeparationRecords.SeparationClock clock,
    ILogger<SeparationPanelBuilder> logger)
{
    public async Task<SeparationPanelModel?> BuildAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var record = await db.SeparationRecords.AsNoTracking().Include(r => r.Contract).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (record is null)
            return null;

        var items = await db.SeparationChecklists.AsNoTracking()
            .Where(c => c.SeparationRecordId == id)
            .OrderBy(c => c.Step)
            .ToListAsync(ct);
        var decision = record.DecisionId is Guid decisionId
            ? await db.AdministrativeDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == decisionId, ct)
            : null;

        WorkflowApprovalStatusDto? approval = null;
        var unavailable = false;
        if (record.WorkflowInstanceId is Guid instanceId)
        {
            try
            {
                using var tenant = WorkflowApprovalSubmission.UseTenant(record.TenantId);
                approval = await approvals.GetStatusAsync(instanceId, ct);
                unavailable = approval is null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The approval state of separation {RecordId} could not be read.", record.Id);
                unavailable = true;
            }
        }

        string? name = null;
        try
        {
            name = (await hrCoreLookup.GetEmployeesByIdsAsync([record.EmployeeId], ct)).FirstOrDefault()?.Label;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Employee name lookup failed for separation {RecordId}.", record.Id);
        }

        var userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var u) ? u : (Guid?)null;
        return new SeparationPanelModel
        {
            Record = record,
            Items = items,
            EmployeeName = name,
            Decision = decision,
            Approval = approval,
            ApprovalUnavailable = unavailable,
            CanWrite = (await authorization.AuthorizeAsync(user, HrManagementPolicies.HrContractsWrite)).Succeeded,
            CanApprove = (await authorization.AuthorizeAsync(user, HrManagementPolicies.HrContractsApprove)).Succeeded,
            IsCreator = userId is not null && userId == record.CreatedBy,
            TodayInDamascus = clock.Today
        };
    }
}
