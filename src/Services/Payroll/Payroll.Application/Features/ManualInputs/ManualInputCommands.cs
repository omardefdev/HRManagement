using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.ManualInputs;

/// <summary>PLAN-54 (D-PAY-6): manual allowances, deductions and adjustments need a second person. Section <c>Payroll:ManualInputs</c>.</summary>
public sealed class PayrollManualInputOptions
{
    public const string SectionName = "Payroll:ManualInputs";

    /// <summary>True everywhere. False is honoured in Development only (elsewhere it is ignored, with a warning at start).</summary>
    public bool RequireApproval { get; set; } = true;
}

/// <summary>Where the setting is honoured: skipping the second person is a Development convenience only (plan §4, rule 2).</summary>
public static class ManualInputApproval
{
    public static bool IsRequired(PayrollManualInputOptions options, IHostEnvironment? environment) =>
        options.RequireApproval || environment?.IsDevelopment() != true;

    /// <summary>The setting asks to skip the approval outside Development: it is not honoured.</summary>
    public static bool IsIgnored(PayrollManualInputOptions options, IHostEnvironment? environment) =>
        !options.RequireApproval && environment?.IsDevelopment() != true;

    public const string IgnoredWarning =
        "Payroll:ManualInputs:RequireApproval=false is honoured in Development only and is ignored in {Environment}: a manual allowance, deduction or adjustment still waits for a second person.";
}

/// <summary>The three kinds of pay input a person may enter by hand.</summary>
public enum ManualInputKind
{
    Allowance = 0,
    Deduction = 1,
    Adjustment = 2,
}

public enum ManualInputDecision
{
    Approved = 0,
    Rejected = 1,
}

/// <summary>Why a manual input command was refused, and so its HTTP answer.</summary>
public enum ManualInputFailure
{
    None = 0,

    /// <summary>A broken input rule: 400.</summary>
    Invalid = 1,

    /// <summary>The input is not waiting for a decision (already decided, or written by the system): 409.</summary>
    Conflict = 2,

    /// <summary>Deciding one's own input: 403.</summary>
    SelfDecision = 3,

    /// <summary>No such input: 404.</summary>
    NotFound = 4,
}

/// <summary>The answer of the manual input commands. A refusal leaves the input as it was.</summary>
public sealed record ManualInputResult(
    bool Success,
    Guid? InputId,
    ManualInputApprovalStatus? Status,
    string? Error,
    ManualInputFailure Failure = ManualInputFailure.None)
{
    public static ManualInputResult Ok(IManualPayInput input) => new(true, input.Id, input.ApprovalStatus, null);

    public static ManualInputResult Fail(ManualInputFailure failure, string error, IManualPayInput? input = null) =>
        new(false, input?.Id, input?.ApprovalStatus, error, failure);

    public int StatusCode => Failure switch
    {
        ManualInputFailure.None => 200,
        ManualInputFailure.Conflict => 409,
        ManualInputFailure.SelfDecision => 403,
        ManualInputFailure.NotFound => 404,
        _ => 400,
    };
}

public static class ManualInputMessages
{
    public const string EmployeeRequired = "الموظف إلزامي";
    public const string NoPayProfile = "لا يوجد ملف أجر للموظف";
    public const string AmountPositive = "المبلغ يجب أن يكون أكبر من صفر";
    public const string AdjustmentNotZero = "مبلغ التسوية لا يكون صفراً";
    public const string AmountScale = "المبلغ بمنزلتين عشريتين على الأكثر";
    public const string ReasonRequired = "السبب إلزامي للمدخل اليدوي";
    public const string ReasonTooLong = "السبب أطول من 500 حرف";
    public const string AllowanceTypeRequired = "نوع البدل إلزامي وموجود في الفهرس";
    public const string DeductionTypeRequired = "نوع الخصم إلزامي ولا يتجاوز 100 حرف";
    public const string EffectiveFromRequired = "تاريخ سريان البدل إلزامي";
    public const string EffectiveToBeforeFrom = "نهاية سريان البدل لا تسبق بدايته";
    public const string NotFound = "المدخل غير موجود";
    public const string NotPending = "المدخل ليس بانتظار الاعتماد";
    public const string SelfDecision = "لا يعتمد المدخل اليدوي ولا يرفضه من أدخله: يقرر فيه شخص ثانٍ";
    public const string RejectNoteRequired = "ملاحظة الرفض إلزامية";
    public const string NoteTooLong = "الملاحظة أطول من 500 حرف";
    public const string UnknownActor = "تعذّر تحديد هوية المستخدم";
}

/// <summary>
/// PLAN-54 (§4): a person enters an allowance, a deduction or an adjustment by hand. It is written <c>PendingApproval</c> and of origin
/// <c>Manual</c>, with who entered it, and counts in no calculation until a second person approves it. The command carries only what
/// a person may say: the employee, the type, the amount, the dates, the reason. Never the run it applies to.
/// </summary>
public sealed record CreateManualInputCommand(
    ManualInputKind Kind,
    Guid EmployeeId,
    decimal Amount,
    string? Reason,
    Guid ActorId,
    Guid? AllowanceTypeId = null,
    string? DeductionType = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    DateOnly? DueDate = null) : IRequest<ManualInputResult>;

/// <summary>PLAN-54 (§4): a second person, who did not enter the input, approves or rejects it (a rejection needs a reason).</summary>
public sealed record DecideManualInputCommand(
    ManualInputKind Kind,
    Guid InputId,
    ManualInputDecision Decision,
    string? Note,
    Guid ActorId) : IRequest<ManualInputResult>;

public sealed class CreateManualInputHandler(
    IPayrollRepository repository,
    IOptions<PayrollManualInputOptions> options,
    IHostEnvironment? environment = null) : IRequestHandler<CreateManualInputCommand, ManualInputResult>
{
    public async Task<ManualInputResult> Handle(CreateManualInputCommand request, CancellationToken cancellationToken)
    {
        if (request.ActorId == Guid.Empty)
            return Invalid(ManualInputMessages.UnknownActor);
        if (request.EmployeeId == Guid.Empty)
            return Invalid(ManualInputMessages.EmployeeRequired);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return Invalid(ManualInputMessages.ReasonRequired);
        if (reason.Length > ManualPayInputRules.ReasonMaxLength)
            return Invalid(ManualInputMessages.ReasonTooLong);
        if (decimal.Round(request.Amount, 2) != request.Amount)
            return Invalid(ManualInputMessages.AmountScale);
        if (request.Kind == ManualInputKind.Adjustment ? request.Amount == 0m : request.Amount <= 0m)
            return Invalid(request.Kind == ManualInputKind.Adjustment ? ManualInputMessages.AdjustmentNotZero : ManualInputMessages.AmountPositive);
        if (!await repository.EmployeePayProfiles.AsNoTracking().AnyAsync(p => p.EmployeeId == request.EmployeeId, cancellationToken))
            return Invalid(ManualInputMessages.NoPayProfile);

        var approved = !ManualInputApproval.IsRequired(options.Value, environment);

        // What every manual input says about itself, whatever its kind: a person entered it, who, and whether it still waits.
        T Entered<T>(T entity) where T : IManualPayInput
        {
            entity.Origin = ManualInputOrigin.Manual;
            entity.CreatedBy = request.ActorId;
            entity.ApprovalStatus = approved ? ManualInputApprovalStatus.Approved : ManualInputApprovalStatus.PendingApproval;
            return entity;
        }

        IManualPayInput input;
        switch (request.Kind)
        {
            case ManualInputKind.Allowance:
                if (request.AllowanceTypeId is not Guid typeId
                    || !await repository.CompensationTypes.AsNoTracking().AnyAsync(t => t.Id == typeId, cancellationToken))
                    return Invalid(ManualInputMessages.AllowanceTypeRequired);
                if (request.EffectiveFrom is not { } from || from == default)
                    return Invalid(ManualInputMessages.EffectiveFromRequired);
                if (request.EffectiveTo is { } to && to < from)
                    return Invalid(ManualInputMessages.EffectiveToBeforeFrom);
                var allowance = Entered(new AllowanceRecord
                {
                    EmployeeId = request.EmployeeId,
                    AllowanceTypeId = typeId,
                    Amount = request.Amount,
                    EffectiveFrom = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    EffectiveTo = request.EffectiveTo?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    Reason = reason,
                });
                await repository.AddAsync(allowance, cancellationToken);
                input = allowance;
                break;
            case ManualInputKind.Deduction:
                var deductionType = request.DeductionType?.Trim();
                if (string.IsNullOrEmpty(deductionType) || deductionType.Length > 100)
                    return Invalid(ManualInputMessages.DeductionTypeRequired);
                var deduction = Entered(new DeductionRecord
                {
                    EmployeeId = request.EmployeeId,
                    DeductionType = deductionType,
                    Amount = request.Amount,
                    Reason = reason,
                    DueDate = request.DueDate,
                });
                await repository.AddAsync(deduction, cancellationToken);
                input = deduction;
                break;
            default:
                var adjustment = Entered(new PayrollAdjustment
                {
                    EmployeeId = request.EmployeeId,
                    Amount = request.Amount,
                    AdjustmentReason = reason,
                });
                await repository.AddAsync(adjustment, cancellationToken);
                input = adjustment;
                break;
        }

        await repository.SaveChangesAsync(cancellationToken);
        return ManualInputResult.Ok(input);
    }

    private static ManualInputResult Invalid(string error) => ManualInputResult.Fail(ManualInputFailure.Invalid, error);
}

public sealed class DecideManualInputHandler(IPayrollRepository repository, IPayrollCalendar calendar)
    : IRequestHandler<DecideManualInputCommand, ManualInputResult>
{
    public async Task<ManualInputResult> Handle(DecideManualInputCommand request, CancellationToken cancellationToken)
    {
        IManualPayInput? input = request.Kind switch
        {
            ManualInputKind.Allowance => await repository.AllowanceRecords.FirstOrDefaultAsync(x => x.Id == request.InputId, cancellationToken),
            ManualInputKind.Deduction => await repository.DeductionRecords.FirstOrDefaultAsync(x => x.Id == request.InputId, cancellationToken),
            _ => await repository.PayrollAdjustments.FirstOrDefaultAsync(x => x.Id == request.InputId, cancellationToken),
        };
        if (input is null)
            return ManualInputResult.Fail(ManualInputFailure.NotFound, ManualInputMessages.NotFound);

        ManualInputResult Refuse(ManualInputFailure failure, string error)
        {
            repository.ClearChangeTracker();
            return ManualInputResult.Fail(failure, error, input);
        }

        if (request.ActorId == Guid.Empty)
            return Refuse(ManualInputFailure.Invalid, ManualInputMessages.UnknownActor);
        // Only a manual input that waits is decided: a system row was never pending, and a decision is not taken twice.
        if (input.Origin != ManualInputOrigin.Manual || input.ApprovalStatus != ManualInputApprovalStatus.PendingApproval)
            return Refuse(ManualInputFailure.Conflict, ManualInputMessages.NotPending);
        if (input.CreatedBy == request.ActorId)
            return Refuse(ManualInputFailure.SelfDecision, ManualInputMessages.SelfDecision);

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (request.Decision == ManualInputDecision.Rejected && note is null)
            return Refuse(ManualInputFailure.Invalid, ManualInputMessages.RejectNoteRequired);
        if (note is { Length: > ManualPayInputRules.DecisionNoteMaxLength })
            return Refuse(ManualInputFailure.Invalid, ManualInputMessages.NoteTooLong);

        var now = calendar.UtcNow;
        input.ApprovalStatus = request.Decision == ManualInputDecision.Approved
            ? ManualInputApprovalStatus.Approved
            : ManualInputApprovalStatus.Rejected;
        input.ApprovedBy = request.ActorId;
        input.ApprovedAt = now;
        input.DecisionNote = note;
        if (input is HRManagement.SharedKernel.Entities.AuditableEntity auditable)
        {
            auditable.UpdatedBy = request.ActorId;
            auditable.UpdatedAt = now;
        }

        await repository.SaveChangesAsync(cancellationToken);
        return ManualInputResult.Ok(input);
    }
}
