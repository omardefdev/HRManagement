using System.Globalization;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Services;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Contracts.Application.Features.SeparationRecords;

/// <summary>The maker submits a draft to HR.Core's engine. The submitter comes from the identity, never from a form.</summary>
public record SubmitSeparationCommand(Guid Id, Guid? SubmitterId) : IRequest<WorkflowApprovalSubmitResult>;

/// <summary>The maker withdraws a pending approval: the engine first; an ended instance gives its outcome, never a draft.</summary>
public record WithdrawSeparationCommand(Guid Id, Guid? ActorId, string? Reason) : IRequest<WorkflowApprovalWithdrawResult>;

/// <summary>The maker reopens a rejected record as a draft, to correct it and submit it again under the next number.</summary>
public record ReopenSeparationCommand(Guid Id, Guid? ActorId) : IRequest<(bool Success, string? Error)>;

/// <summary>
/// PLAN-60 (§2, D-SEP-4): the separation record's side of PLAN-46's contract. A draft is submitted to <c>SEPARATION-APPROVAL</c> or
/// <c>-SENSITIVE</c> with its creator and its submitter excluded; a pending record is withdrawn through the engine first; the outcome, from
/// the event, the sweep or a withdrawal that met the end, is applied in <see cref="ApplyOutcomeAsync"/> only. HR.Contracts has no
/// approval button: the approval is in HR.Core's inbox.
/// </summary>
public sealed class SeparationApprovalService(
    IContractsRepository repository,
    IWorkflowApprovalClient client,
    SeparationRecordFactory factory,
    IHrCoreLookupClient hrCoreLookup,
    IOptions<SeparationOptions> options,
    ILogger<SeparationApprovalService> logger)
{
    public const string NotDraftMessage = "يُقدَّم سجل الفصل للاعتماد وهو مسودة فقط.";
    public const string NotPendingMessage = "لا يوجد اعتماد جارٍ لسجل الفصل هذا.";
    public const string NotRejectedMessage = "يُعاد فتح سجل الفصل المرفوض فقط.";
    public const string NoSubmitterMessage = "تعذّر تحديد المستخدم الحالي — لم يُقدَّم الطلب.";
    public const string RefusedApprovalReason = "رُفض الاعتماد: اعتمده منشئ السجل أو مقدّم الطلب نفسه.";
    public const string DefaultWithdrawReason = "سحب من مقدّم الطلب";

    public async Task<WorkflowApprovalSubmitResult> SubmitAsync(Guid id, Guid? submitter, CancellationToken ct = default)
    {
        if (submitter is not Guid by || by == Guid.Empty)
            return new WorkflowApprovalSubmitResult(false, NoSubmitterMessage);

        var record = await repository.GetSeparationRecordAsync(id, ct);
        if (record is null)
            return new WorkflowApprovalSubmitResult(false, "سجل الفصل غير موجود.");
        if (record.Status != SeparationStatus.Draft)
            return new WorkflowApprovalSubmitResult(false, NotDraftMessage);

        // P-6 again: the employee's service has not ended, no other open record, the contract still his.
        var check = await factory.CheckSubjectAsync(record.EmployeeId, record.ContractId, record.Id, ct);
        if (!check.Success)
            return new WorkflowApprovalSubmitResult(false, check.Error);
        record.ContractId ??= check.ContractId;

        var name = await EmployeeNameAsync(record.EmployeeId, ct);
        var orgUnit = await repository.EmployeeOrgUnitSnapshots
            .Where(s => s.EmployeeId == record.EmployeeId)
            .Select(s => s.PrimaryOrgUnitId)
            .FirstOrDefaultAsync(ct);
        var code = SeparationApprovalRouting.CodeFor(record.SeparationType, options.Value.Approval);

        using var tenant = WorkflowApprovalSubmission.UseTenant(record.TenantId);
        return await WorkflowApprovalSubmission.SubmitAsync(client, record.SubmissionNo, new WorkflowApprovalSubmitSteps
        {
            Request = number => StartRequest(record, by, number, code, name, orgUnit),
            LinkAndSaveAsync = async (instanceId, number, token) =>
            {
                // Rule 3: PendingApproval is saved only with its instance, in this one save.
                await SeparationStatusSync.TransitionAsync(repository, record, SeparationStatus.PendingApproval, by, "تقديم للاعتماد", token,
                    new SeparationSubmission(instanceId, code, number));
                await repository.SaveChangesAsync(token);
            },
            SpendNumberAsync = async (number, token) =>
            {
                record.SubmissionNo = number;
                await repository.SaveChangesAsync(token);
            },
            IsLinkedToAsync = (instanceId, token) =>
                repository.SeparationRecords.AsNoTracking().AnyAsync(
                    r => r.Id == record.Id && r.WorkflowInstanceId == instanceId && r.Status != SeparationStatus.Draft, token),
            ApplyOutcomeAsync = ApplyOutcomeAsync
        }, logger, ct);
    }

    public async Task<WorkflowApprovalWithdrawResult> WithdrawAsync(Guid id, Guid? actor, string? reason, CancellationToken ct = default)
    {
        var record = await repository.GetSeparationRecordAsync(id, ct);
        if (record is null)
            return new WorkflowApprovalWithdrawResult(false, "سجل الفصل غير موجود.");
        return await WithdrawAsync(record, actor, reason, ct);
    }

    public async Task<WorkflowApprovalWithdrawResult> WithdrawAsync(SeparationRecord record, Guid? actor, string? reason, CancellationToken ct = default)
    {
        if (record.Status != SeparationStatus.PendingApproval || record.WorkflowInstanceId is not Guid instanceId)
            return new WorkflowApprovalWithdrawResult(false, NotPendingMessage);

        using var tenant = WorkflowApprovalSubmission.UseTenant(record.TenantId);
        return await WorkflowApprovalSubmission.WithdrawAsync(
            client, SeparationApprovalRouting.EntityType, record.Id, instanceId, actor,
            string.IsNullOrWhiteSpace(reason) ? DefaultWithdrawReason : reason.Trim(), ApplyOutcomeAsync, ct);
    }

    public async Task<(bool Success, string? Error)> ReopenAsync(Guid id, Guid? actor, CancellationToken ct = default)
    {
        var record = await repository.GetSeparationRecordAsync(id, ct);
        if (record is null)
            return (false, null);
        if (record.Status != SeparationStatus.Rejected)
            return (false, NotRejectedMessage);
        await SeparationStatusSync.TransitionAsync(repository, record, SeparationStatus.Draft, actor, "إعادة فتح بعد الرفض", ct);
        await repository.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// The one place an outcome changes a record, for the consumer, the sweep and the withdrawal alike. The guard keeps a record that is
    /// not pending, or not linked to the instance, as it is; an approval that names the creator or the submitter is refused, logged and
    /// counted, and the record closed as rejected (left pending, the sweep would read it again on every cycle).
    /// </summary>
    public async Task ApplyOutcomeAsync(IWorkflowOutcomeDecided outcome, CancellationToken ct = default)
    {
        var record = await repository.SeparationRecords.FirstOrDefaultAsync(r => r.Id == outcome.EntityId, ct);
        var pending = record is { Status: SeparationStatus.PendingApproval };
        if (!WorkflowOutcomeGuard.ShouldApply(record?.WorkflowInstanceId, pending, outcome, logger))
            return;

        switch (outcome.Outcome)
        {
            case WorkflowApprovalOutcomes.Approved:
                if (WorkflowOutcomeGuard.HasExcludedDecider(outcome, [record!.CreatedBy, record.SubmittedBy], logger))
                {
                    logger.LogError("Separation {RecordId}: an approval by its creator or submitter was refused.", record.Id);
                    await SeparationStatusSync.TransitionAsync(repository, record, SeparationStatus.Rejected, outcome.DecidedByUserId,
                        RefusedApprovalReason, ct);
                    break;
                }

                await SeparationStatusSync.TransitionAsync(repository, record, SeparationStatus.Approved, outcome.DecidedByUserId,
                    "اعتماد محرك سير العمل", ct);
                break;
            case WorkflowApprovalOutcomes.Rejected:
                await SeparationStatusSync.TransitionAsync(repository, record!, SeparationStatus.Rejected, outcome.DecidedByUserId,
                    string.IsNullOrWhiteSpace(outcome.Reason) ? "رُفض في محرك سير العمل" : outcome.Reason, ct);
                break;
            default:
                // Withdrawn: back to a draft that can be corrected and submitted again under the next number.
                await SeparationStatusSync.TransitionAsync(repository, record!, SeparationStatus.Draft, outcome.DecidedByUserId,
                    string.IsNullOrWhiteSpace(outcome.Reason) ? "سحب الاعتماد" : outcome.Reason, ct);
                break;
        }

        await repository.SaveChangesAsync(ct);
        logger.LogInformation("Separation {RecordId} is {Status} after instance {InstanceId}.", record!.Id, record.Status, outcome.InstanceId);
    }

    /// <summary>
    /// The creator and the submitter are excluded from every step. A record no person created (an inbound request, the seed) has no creator:
    /// the submitter alone is excluded, and the subject employee through <c>SubjectEmployeeId</c>.
    /// </summary>
    public static IReadOnlyCollection<Guid> ExcludedApprovers(SeparationRecord record, Guid submitter) =>
        new[] { record.CreatedBy, submitter }
            .Where(id => id is not null && id != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

    private static WorkflowApprovalStartRequest StartRequest(
        SeparationRecord record, Guid submitter, int number, string code, string name, Guid? orgUnit) =>
        new(
            SeparationApprovalRouting.EntityType,
            record.Id,
            code,
            $"{SeparationApprovalRouting.EntityType}:{record.Id:D}:{number}",
            ExcludedApprovers(record, submitter),
            orgUnit,
            // No national number: the employee's name and the type.
            $"إنهاء خدمة — {name} — {TypeLabel(record.SeparationType)}",
            $"/ui/hr-contracts/SeparationRecords/Details/{record.Id:D}",
            new Dictionary<string, string>
            {
                ["separationType"] = TypeLabel(record.SeparationType),
                ["effectiveDate"] = record.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["employeeId"] = record.EmployeeId.ToString("D")
            },
            record.EmployeeId);

    private async Task<string> EmployeeNameAsync(Guid employeeId, CancellationToken ct)
    {
        try
        {
            var found = await hrCoreLookup.GetEmployeesByIdsAsync([employeeId], ct);
            return found.FirstOrDefault()?.Label is { Length: > 0 } label ? label : "موظف";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The employee's name for separation {EmployeeId} could not be read; the title says «موظف».", employeeId);
            return "موظف";
        }
    }

    public static string TypeLabel(SeparationType type) => type switch
    {
        SeparationType.Resignation => "استقالة",
        SeparationType.Termination => "إنهاء",
        SeparationType.EndOfContract => "انتهاء عقد",
        SeparationType.Retirement => "تقاعد",
        SeparationType.Medical => "طبية",
        SeparationType.Judicial => "قضائية",
        SeparationType.Disciplinary => "تأديبية",
        _ => type.ToString()
    };
}

public class SubmitSeparationHandler(SeparationApprovalService approvals) : IRequestHandler<SubmitSeparationCommand, WorkflowApprovalSubmitResult>
{
    public Task<WorkflowApprovalSubmitResult> Handle(SubmitSeparationCommand request, CancellationToken cancellationToken) =>
        approvals.SubmitAsync(request.Id, request.SubmitterId, cancellationToken);
}

public class WithdrawSeparationHandler(SeparationApprovalService approvals) : IRequestHandler<WithdrawSeparationCommand, WorkflowApprovalWithdrawResult>
{
    public Task<WorkflowApprovalWithdrawResult> Handle(WithdrawSeparationCommand request, CancellationToken cancellationToken) =>
        approvals.WithdrawAsync(request.Id, request.ActorId, request.Reason, cancellationToken);
}

public class ReopenSeparationHandler(SeparationApprovalService approvals) : IRequestHandler<ReopenSeparationCommand, (bool Success, string? Error)>
{
    public Task<(bool Success, string? Error)> Handle(ReopenSeparationCommand request, CancellationToken cancellationToken) =>
        approvals.ReopenAsync(request.Id, request.ActorId, cancellationToken);
}
