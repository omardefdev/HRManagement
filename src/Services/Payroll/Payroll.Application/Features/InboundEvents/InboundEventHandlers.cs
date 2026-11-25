using HRManagement.MessageContracts.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.InboundEvents;

public record HandleContractActivatedCommand(
    Guid ContractId,
    Guid EmployeeId,
    string ContractType,
    DateTime StartDate,
    decimal? BaseSalary,
    string? PayGradeCode) : IRequest;

/// <remarks>
/// PAY-029: the grade is resolved in the salary scale in force at the contract start; the salary is the agreed one
/// (IContractActivated.BaseSalary from the offer) or the grade's base. A salary outside the grade bounds, or no salary
/// at all, still creates the profile and places a review hold (FraudAlert + PaymentHold, run-independent) so nothing is
/// paid before HR/payroll looks at it.
/// <para>
/// PLAN-55 (§3): this handler no longer writes a pay profile. The version is made by PLAN-53's applier
/// (<c>ApplySystemChangeAsync</c>, source <c>ContractActivated</c>, the contract's id in <c>SourceReference</c>), so every activation
/// is an <c>Applied</c> request row, the new version keeps the unit and the regime, and the previous one closes the day before the
/// contract starts. What the handler stages here (the zero adjustment, a PAY-029 hold) is written by the applier's one save. A
/// contract already applied changes nothing (a message delivered twice). A refusal no retry would change (the start date lies inside
/// a version that held those days) is turned into a review hold and the message is consumed; a concurrent save is thrown, and the
/// redelivery finds the state settled.
/// </para>
/// </remarks>
public class HandleContractActivatedHandler(
    IPayrollRepository repository,
    IOptions<PayrollActivationOptions>? options = null,
    ILogger<HandleContractActivatedHandler>? logger = null,
    IPayProfileChangeApplier? applier = null)
    : IRequestHandler<HandleContractActivatedCommand>
{
    public const string RuleOutOfRange = "PAY029-RANGE";
    public const string RuleNoSalary = "PAY029-NOSAL";
    public const string OutOfRangeReasonAr = "الراتب خارج حدود الدرجة";
    public const string NoSalaryReasonAr = "لا يوجد راتب أساسي عند تفعيل العقد";
    public const string AlertCategory = "PayProfileActivation";

    /// <summary>PLAN-55: the review hold of an activation the applier refused (PAY-029's pattern: the rule code starts the reason).</summary>
    public const string RuleActivationRefused = "PAY055-ACTIVATION";
    public const string ActivationRefusedReasonAr = "تعذّر تطبيق تفعيل العقد على ملف الأجر";

    private readonly ILogger _logger = logger ?? NullLogger<HandleContractActivatedHandler>.Instance;

    /// <summary>The host injects the registered applier; a handler built by hand (a unit test) gets one with no bus.</summary>
    private readonly IPayProfileChangeApplier _applier = applier ?? PayProfileChangeApplier.Detached(repository);

    public async Task Handle(HandleContractActivatedCommand request, CancellationToken cancellationToken)
    {
        // A message delivered twice changes nothing the second time: this contract's activation is already an applied request.
        var contractReference = request.ContractId.ToString("D");
        if (await repository.PayProfileChangeRequests.AsNoTracking().AnyAsync(
                r => r.EmployeeId == request.EmployeeId
                     && r.Source == PayProfileChangeSource.ContractActivated
                     && r.SourceReference == contractReference
                     && r.State == PayProfileChangeState.Applied,
                cancellationToken))
            return;

        var grade = await ResolveGradeAsync(request, cancellationToken);
        var salary = request.BaseSalary ?? grade?.BaseSalary;
        var requireSalary = options?.Value.RequireBaseSalaryOnActivation ?? false;
        var baseSalary = salary ?? 0;

        // Staged in the unit of work: the applier's one save writes them with the version, the request row and the event (PLAN-40).
        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = 0,
            AdjustmentReason = $"تفعيل عقد ({request.ContractType}) — ContractId={request.ContractId}"
        }, cancellationToken);

        if (salary is null)
        {
            _logger.LogWarning(
                "ContractActivated {ContractId}: no base salary and no grade for employee {EmployeeId}; profile {State}, review hold raised.",
                request.ContractId, request.EmployeeId, requireSalary ? "inactive" : "active with 0");
            await RaiseReviewHoldAsync(request, RuleNoSalary, NoSalaryReasonAr, $"{NoSalaryReasonAr} — العقد {request.ContractId}", cancellationToken);
        }
        else if (grade is not null && (salary < grade.MinSalary || salary > grade.MaxSalary))
        {
            _logger.LogWarning(
                "ContractActivated {ContractId}: salary outside grade {GradeCode} bounds for employee {EmployeeId}; review hold raised.",
                request.ContractId, grade.GradeCode, request.EmployeeId);
            await RaiseReviewHoldAsync(
                request,
                RuleOutOfRange,
                OutOfRangeReasonAr,
                $"{OutOfRangeReasonAr} {grade.GradeCode}: {salary:0.##} خارج [{grade.MinSalary:0.##} – {grade.MaxSalary:0.##}] — العقد {request.ContractId}",
                cancellationToken);
        }

        // PAY-029, unchanged: the agreed salary when it is positive, else the salary of the profile in force (0 on a first activation);
        // the resolved grade, else the grade in force; a first profile is inactive only when no salary is known and the option asks so.
        decimal? proposedSalary = baseSalary > 0 ? baseSalary : null;
        var kind = (proposedSalary, grade) switch
        {
            (not null, not null) => PayProfileChangeKind.BaseSalaryAndGrade,
            (null, not null) => PayProfileChangeKind.PayGrade,
            _ => PayProfileChangeKind.BaseSalary,
        };
        var result = await _applier.ApplySystemChangeAsync(
            request.EmployeeId,
            kind,
            PayProfileChangeSource.ContractActivated,
            contractReference,
            proposedSalary,
            grade?.Id,
            // The contract's start is a calendar date (HR.Contracts sends it at 00:00 UTC): stored as that date, never shifted.
            DateOnly.FromDateTime(request.StartDate),
            new PayProfileContractActivation(
                FirstVersionIsActive: salary is not null || !requireSalary,
                HistoryReason: $"تفعيل عقد {request.ContractType}"),
            cancellationToken);
        if (result.Success)
            return;

        // The applier saved nothing, and nothing staged above is kept.
        repository.ClearChangeTracker();
        if (result.Failure == PayProfileChangeFailure.Conflict)
        {
            // Another save of the same employee won between the read and the save. Thrown as the concurrency failure it is: the bus
            // retries that (MessagingFaults.IsTransient) and sends any other InvalidOperationException to the error queue. The
            // redelivery meets the settled state: either this contract is applied by then (nothing to do) or its version is made
            // over the new one.
            throw new DbUpdateConcurrencyException(
                $"ContractActivated {request.ContractId}: the pay profile of employee {request.EmployeeId} changed concurrently; the message is retried.");
        }

        // No retry changes this answer: the activation waits for a person, behind a review hold that says why, and the message is consumed.
        _logger.LogWarning(
            "ContractActivated {ContractId}: the activation was not applied to the pay profile of employee {EmployeeId} ({Failure}); review hold raised.",
            request.ContractId, request.EmployeeId, result.Failure);
        // One hold per refused contract (the contract's id is in the hold's reason): a second contract refused while the first
        // still waits is recorded too; the same contract delivered again places no second hold.
        await RaiseReviewHoldAsync(
            request,
            RuleActivationRefused,
            $"{ActivationRefusedReasonAr} — العقد {request.ContractId:D}",
            $"{ActivationRefusedReasonAr}: {result.Error} — العقد {request.ContractId}",
            cancellationToken,
            sameReasonOnly: true);
        await repository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The grade in the active scale in force at the contract start (latest EffectiveFrom). Without a dated match the
    /// pre-PAY-029 lookup by code is used, with a warning.
    /// </summary>
    private async Task<PayGrade?> ResolveGradeAsync(HandleContractActivatedCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.PayGradeCode))
            return null;

        var start = request.StartDate;
        var dated = await repository.PayGrades
            .Include(g => g.SalaryScale)
            .Where(g => g.GradeCode == request.PayGradeCode
                        && g.SalaryScale.IsActive
                        && g.SalaryScale.EffectiveFrom <= start
                        && (g.SalaryScale.EffectiveTo == null || g.SalaryScale.EffectiveTo >= start))
            .OrderByDescending(g => g.SalaryScale.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        if (dated is not null)
            return dated;

        var any = await repository.PayGrades
            .FirstOrDefaultAsync(g => g.GradeCode == request.PayGradeCode, cancellationToken);
        if (any is not null)
            _logger.LogWarning(
                "ContractActivated {ContractId}: no active salary scale in force on {StartDate:yyyy-MM-dd} for grade {GradeCode}; using an undated match.",
                request.ContractId, start, request.PayGradeCode);
        return any;
    }

    /// <summary>
    /// "Review before pay": an Active PaymentHold without a payroll run blocks this employee in every disbursement batch
    /// (PaymentDisbursementGate) until released; the linked FraudAlert puts it in the existing review queue.
    /// One open hold per rule and employee (redelivery-safe).
    /// </summary>
    private async Task RaiseReviewHoldAsync(
        HandleContractActivatedCommand request,
        string ruleId,
        string reasonAr,
        string details,
        CancellationToken cancellationToken,
        bool sameReasonOnly = false)
    {
        // PAY-029: one open hold per rule and employee. PLAN-55's refusal hold: one per rule, employee and reason (its contract).
        var reason = $"{ruleId}: {reasonAr}";
        if (await repository.PaymentHolds.AnyAsync(
                h => h.EmployeeId == request.EmployeeId
                     && h.Status == PaymentHoldStatus.Active
                     && h.HoldType == PaymentHoldType.Manual
                     && (sameReasonOnly ? h.Reason == reason : h.Reason.StartsWith(ruleId)),
                cancellationToken))
            return;

        var alert = new FraudAlert
        {
            EmployeeId = request.EmployeeId,
            RuleId = ruleId,
            Category = AlertCategory,
            Severity = FraudAlertSeverity.Medium,
            Status = FraudAlertStatus.Open,
            Details = details,
            DetectedAtUtc = DateTime.UtcNow
        };
        var hold = new PaymentHold
        {
            EmployeeId = request.EmployeeId,
            FraudAlertId = alert.Id,
            Reason = reason,
            HoldType = PaymentHoldType.Manual,
            Status = PaymentHoldStatus.Active,
            HeldAtUtc = DateTime.UtcNow
        };
        alert.PaymentHoldId = hold.Id;
        await repository.AddAsync(alert, cancellationToken);
        await repository.AddAsync(hold, cancellationToken);
    }
}

public record HandleSeparationCompletedCommand(
    Guid EmployeeId,
    string SeparationType,
    DateTime EffectiveDate,
    Guid? SeparationRecordId,
    int RemainingLeaveDays = 0,
    string? PayrollRegimeCode = null) : IRequest;

public class HandleSeparationCompletedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleSeparationCompletedCommand>
{
    public async Task Handle(HandleSeparationCompletedCommand request, CancellationToken cancellationToken)
    {
        if (request.SeparationRecordId.HasValue &&
            await repository.EndOfServiceSettlements.AnyAsync(
                e => e.SeparationRecordId == request.SeparationRecordId, cancellationToken))
            return;

        var profile = await repository.GetActiveProfileAsync(request.EmployeeId, cancellationToken);
        if (profile is not null)
        {
            await repository.AddAsync(
                EmployeePayProfileRules.CreateHistorySnapshot(profile, $"إنهاء خدمة {request.SeparationType}"),
                cancellationToken);

            profile.EffectiveTo = request.EffectiveDate;
            profile.IsActive = false;

            var outstanding = await repository.DeductionRecords
                .Where(d => d.EmployeeId == request.EmployeeId
                            && d.AppliedInRunId == null
                            && d.DeductionType == "AdvanceRecovery"
                            // PLAN-54 (D-PAY-6): a manual deduction counts only after a second person approved it.
                            && d.ApprovalStatus == ManualInputApprovalStatus.Approved)
                .SumAsync(d => d.Amount, cancellationToken);

            var config = await ResolveConfigurationAsync(repository, request.PayrollRegimeCode, cancellationToken);
            var settlement = EndOfServiceCalculation.CreateSettlement(
                request.EmployeeId,
                request.SeparationRecordId,
                profile.EffectiveFrom,
                request.EffectiveDate,
                profile.BaseSalary,
                request.RemainingLeaveDays,
                outstanding,
                config: config);

            await repository.AddAsync(settlement, cancellationToken);

            if (outstanding > 0)
            {
                await repository.AddAsync(new DeductionRecord
                {
                    EmployeeId = request.EmployeeId,
                    DeductionType = "AdvanceFinalRecovery",
                    Amount = outstanding,
                    Reason = $"استرداد سلف متبقية عند إنهاء الخدمة — SeparationCompleted-{request.EmployeeId}-{request.EffectiveDate:yyyyMMdd}"
                }, cancellationToken);
            }
        }

        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = 0,
            AdjustmentReason = $"إنهاء خدمة ({request.SeparationType}) — SeparationCompleted-{request.EmployeeId}-{request.EffectiveDate:yyyyMMdd}"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }

    private static async Task<PayrollConfiguration?> ResolveConfigurationAsync(
        IPayrollRepository repository,
        string? payrollRegimeCode,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(payrollRegimeCode))
        {
            var byRegime = await repository.GetConfigurationByRegimeCodeAsync(payrollRegimeCode, cancellationToken);
            if (byRegime is not null)
                return byRegime;
        }

        return await repository.GetActiveConfigurationAsync(cancellationToken);
    }
}

public record HandleTransferExecutedCommand(
    Guid EmployeeId,
    Guid FromOrgUnitId,
    Guid ToOrgUnitId,
    DateTime ExecutedAt,
    decimal? SalaryChange,
    decimal? AllowanceChange,
    string? PayrollRegimeCode = null) : IRequest;

public class HandleTransferExecutedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleTransferExecutedCommand>
{
    public async Task Handle(HandleTransferExecutedCommand request, CancellationToken cancellationToken)
    {
        var marker = $"Transfer-{request.EmployeeId}-{request.ExecutedAt:yyyyMMdd}";
        var salaryChange = request.SalaryChange.GetValueOrDefault();
        var allowanceChange = request.AllowanceChange.GetValueOrDefault();
        var totalChange = salaryChange + allowanceChange;

        if (totalChange != 0)
        {
            await repository.AddAsync(new PayrollAdjustment
            {
                EmployeeId = request.EmployeeId,
                Amount = totalChange,
                AdjustmentReason = $"نقل من {request.FromOrgUnitId} إلى {request.ToOrgUnitId} — {marker}"
            }, cancellationToken);
        }

        var profile = await repository.GetActiveProfileAsync(request.EmployeeId, cancellationToken);
        if (profile is not null)
        {
            profile.OrgUnitId = request.ToOrgUnitId;
            if (!string.IsNullOrWhiteSpace(request.PayrollRegimeCode))
                profile.RegimeCode = request.PayrollRegimeCode;
            if (salaryChange != 0)
            {
                await repository.AddAsync(
                    EmployeePayProfileRules.CreateHistorySnapshot(profile, marker),
                    cancellationToken);

                profile.BaseSalary += salaryChange;
            }
        }

        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = 0,
            AdjustmentReason = $"تسجيل نقل — {marker}"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record HandleLeaveApprovedCommand(
    Guid LeaveRequestId,
    Guid EmployeeId,
    string LeaveType,
    int DurationDays,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null) : IRequest;

public class HandleLeaveApprovedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleLeaveApprovedCommand>
{
    public async Task Handle(HandleLeaveApprovedCommand request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.LeaveType, "Unpaid", StringComparison.OrdinalIgnoreCase))
        {
            await repository.AddAsync(new PayrollAdjustment
            {
                EmployeeId = request.EmployeeId,
                Amount = 0,
                AdjustmentReason = $"اعتماد إجازة ({request.LeaveType}, {request.DurationDays} يوم) — LeaveRequestId={request.LeaveRequestId}"
            }, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return;
        }

        var profile = await repository.GetActiveProfileAsync(request.EmployeeId, cancellationToken);
        var config = await repository.GetActiveConfigurationAsync(cancellationToken);
        decimal amount = 0;

        if (profile is not null && request.DurationDays > 0)
        {
            if (config?.ProrationDaysBasis == Domain.Enums.ProrationDaysBasis.Calendar30)
            {
                var divisor = config.DailyRateDivisor > 0 ? config.DailyRateDivisor : 30;
                amount = PayrollPeriodRules.ProrateMonthlyAmount(profile.BaseSalary, divisor, request.DurationDays);
            }
            else
            {
                amount = PayrollPeriodRules.ProrateMonthlyAmount(profile.BaseSalary, 22, request.DurationDays);
            }

            // PAY-DD-001: exactly one deduction holder per unpaid leave. Writing both a
            // DeductionRecord("UnpaidLeave") and an AbsenceDeduction of the same amount made
            // AbsenceRule charge the employee twice for the same leave.
            // AbsenceDeduction is the canonical carrier: it holds the real leave period, so the
            // partition loader can scope it to the run being calculated.
            var sourceReference = $"LeaveRequestId={request.LeaveRequestId}";
            var alreadyRecorded = await repository.AbsenceDeductions
                .AnyAsync(a => a.SourceReference == sourceReference, cancellationToken);

            if (!alreadyRecorded)
            {
                // Period must come from the leave itself. Using UtcNow attributed the absence to
                // the approval date, which lands in the wrong payroll period whenever leave is
                // approved in advance.
                var periodStart = request.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
                var periodEnd = request.EndDate ?? periodStart.AddDays(Math.Max(request.DurationDays - 1, 0));

                await repository.AddAsync(new AbsenceDeduction
                {
                    EmployeeId = request.EmployeeId,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    AbsentDays = request.DurationDays,
                    Amount = amount,
                    SourceReference = sourceReference,
                    Reason = $"إجازة بدون راتب — {sourceReference}"
                }, cancellationToken);
            }
        }

        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = 0,
            AdjustmentReason = $"اعتماد إجازة ({request.LeaveType}, {request.DurationDays} يوم) — LeaveRequestId={request.LeaveRequestId}"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record HandleLeaveCancelledCommand(
    Guid LeaveRequestId,
    Guid EmployeeId,
    string LeaveType,
    int DurationDays,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null) : IRequest;

public class HandleLeaveCancelledHandler(IPayrollRepository repository)
    : IRequestHandler<HandleLeaveCancelledCommand>
{
    public async Task Handle(HandleLeaveCancelledCommand request, CancellationToken cancellationToken)
    {
        var sourceReference = $"LeaveRequestId={request.LeaveRequestId}";

        if (string.Equals(request.LeaveType, "Unpaid", StringComparison.OrdinalIgnoreCase))
        {
            var absence = await repository.AbsenceDeductions
                .FirstOrDefaultAsync(a => a.SourceReference == sourceReference, cancellationToken);

            if (absence is not null && absence.AppliedInRunId is null)
                repository.Remove(absence);
        }

        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = 0,
            AdjustmentReason = $"إلغاء إجازة ({request.LeaveType}, {request.DurationDays} يوم) — {sourceReference}"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record HandleAdvanceApprovedCommand(
    Guid AdvanceRequestId,
    Guid EmployeeId,
    decimal Amount,
    decimal MonthlyInstallment,
    int NumberOfInstallments) : IRequest;

public class HandleAdvanceApprovedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleAdvanceApprovedCommand>
{
    public async Task Handle(HandleAdvanceApprovedCommand request, CancellationToken cancellationToken)
    {
        var installments = request.NumberOfInstallments > 0 ? request.NumberOfInstallments : 1;
        var marker = $"AdvanceRequestId={request.AdvanceRequestId}";

        var alreadyScheduled = await repository.DeductionRecords
            .AnyAsync(d => d.SourceReference == marker, cancellationToken);
        if (alreadyScheduled)
            return;

        var total = request.Amount;
        var monthly = decimal.Round(total / installments, 2, MidpointRounding.AwayFromZero);

        // Instalments fall due on successive months, so a run recovers only what is due by its
        // period end rather than the whole plan at once.
        var firstDueDate = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var i = 0; i < installments; i++)
        {
            // The last instalment absorbs the rounding remainder, otherwise the recovered total
            // silently falls short of the advance (10000 over 3 recovered only 9999.99).
            var amount = i == installments - 1
                ? total - (monthly * (installments - 1))
                : monthly;

            await repository.AddAsync(new DeductionRecord
            {
                EmployeeId = request.EmployeeId,
                DeductionType = "AdvanceRecovery",
                Amount = amount,
                DueDate = firstDueDate.AddMonths(i),
                InstallmentNumber = i + 1,
                SourceReference = marker,
                Reason = $"قسط سلفة {i + 1}/{installments} — {marker}"
            }, cancellationToken);
        }

        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = 0,
            AdjustmentReason = $"تسجيل سلفة معتمدة ({installments} قسط × {monthly}) — {marker} Amount={request.Amount}"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record HandleSecondmentExecutedCommand(Guid SecondmentRequestId, Guid EmployeeId, string HostOrg) : IRequest;

public class HandleSecondmentExecutedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleSecondmentExecutedCommand>
{
    public async Task Handle(HandleSecondmentExecutedCommand request, CancellationToken cancellationToken)
    {
        var config = await repository.GetActiveConfigurationAsync(cancellationToken);
        var profile = await repository.GetActiveProfileAsync(request.EmployeeId, cancellationToken);
        var marker = $"Secondment-{request.SecondmentRequestId}";

        decimal amount = 0;
        if (config != null && config.SecondmentAllowancePercent > 0 && profile != null)
            amount = decimal.Round(profile.BaseSalary * config.SecondmentAllowancePercent / 100m, 2);

        if (amount != 0)
        {
            await repository.AddAsync(new PayrollAdjustment
            {
                EmployeeId = request.EmployeeId,
                Amount = amount,
                AdjustmentReason = $"بدل انتداب — {marker}"
            }, cancellationToken);
        }

        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = 0,
            AdjustmentReason = $"انتداب لدى {request.HostOrg} — {marker}"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record HandleMissionApprovedCommand(Guid MissionFileId, Guid EmployeeId, string Destination) : IRequest;

public class HandleMissionApprovedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleMissionApprovedCommand>
{
    public async Task Handle(HandleMissionApprovedCommand request, CancellationToken cancellationToken)
    {
        var config = await repository.GetActiveConfigurationAsync(cancellationToken);
        var marker = $"Mission-{request.MissionFileId}";
        var amount = config?.MissionAllowanceAmount ?? 0;

        if (amount != 0)
        {
            await repository.AddAsync(new PayrollAdjustment
            {
                EmployeeId = request.EmployeeId,
                Amount = amount,
                AdjustmentReason = $"بدل ابتعاث — {request.Destination} — {marker}"
            }, cancellationToken);
        }

        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = 0,
            AdjustmentReason = $"مهمة — {request.Destination} — {marker}"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record HandleDisciplinaryActionCommand(
    Guid ActionId,
    Guid EmployeeId,
    string ActionType,
    int PenaltyDays,
    string? Notes) : IRequest;

public class HandleDisciplinaryActionHandler(IPayrollRepository repository)
    : IRequestHandler<HandleDisciplinaryActionCommand>
{
    public async Task Handle(HandleDisciplinaryActionCommand request, CancellationToken cancellationToken)
    {
        var marker = $"DisciplinaryAction-{request.ActionId}";
        var profile = await repository.GetActiveProfileAsync(request.EmployeeId, cancellationToken);

        if (profile is not null && request.PenaltyDays > 0
            && request.ActionType is "Suspension" or "Dismissal")
        {
            var config = await repository.GetActiveConfigurationAsync(cancellationToken);
            decimal amount;
            if (config?.ProrationDaysBasis == Domain.Enums.ProrationDaysBasis.Calendar30)
            {
                var divisor = config.DailyRateDivisor > 0 ? config.DailyRateDivisor : 30;
                amount = PayrollPeriodRules.ProrateMonthlyAmount(profile.BaseSalary, divisor, request.PenaltyDays);
            }
            else
            {
                amount = PayrollPeriodRules.ProrateMonthlyAmount(profile.BaseSalary, 22, request.PenaltyDays);
            }

            await repository.AddAsync(new DeductionRecord
            {
                EmployeeId = request.EmployeeId,
                DeductionType = "DisciplinaryDeduction",
                Amount = amount,
                Reason = $"خصم تأديبي ({request.PenaltyDays} يوم) — {request.ActionType} — {marker}"
            }, cancellationToken);
        }

        if (request.ActionType is "Dismissal" && profile is not null)
        {
            profile.IsActive = false;
            profile.EffectiveTo = DateTime.UtcNow;
        }

        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = 0,
            AdjustmentReason = $"إجراء تأديبي: {request.ActionType} — {request.Notes} — {marker}"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record HandleEmployeeCreatedCommand(
    Guid EmployeeId,
    Guid? OrgUnitId = null,
    string? PayrollRegimeCode = null) : IRequest;

/// <summary>
/// PLAN-55 (§3): a new employee's first pay profile — inactive, unpriced, carrying the unit and the regime — is made by
/// <c>EmployeePayProfileVersioning.CreateNextVersion</c>, dated today in the payroll calendar. When the event arrives after a contract
/// was activated (or after any other profile exists), it is no longer ignored: it fills the unit and the regime of the employee's
/// current profile <b>where they are empty</b>, and never overwrites a value a later event set (a transfer).
/// </summary>
public class HandleEmployeeCreatedHandler(IPayrollRepository repository, IPayrollCalendar? calendar = null)
    : IRequestHandler<HandleEmployeeCreatedCommand>
{
    private readonly IPayrollCalendar _calendar = calendar ?? new PayrollCalendar(TimeProvider.System);

    public async Task Handle(HandleEmployeeCreatedCommand request, CancellationToken cancellationToken)
    {
        var profiles = await repository.EmployeePayProfiles
            .Where(p => p.EmployeeId == request.EmployeeId)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            var step = EmployeePayProfileVersioning.CreateNextVersion(
                request.EmployeeId,
                profiles,
                new PayProfileVersionChange(
                    _calendar.Today, OrgUnitId: request.OrgUnitId, RegimeCode: request.PayrollRegimeCode, IsActive: false));
            await repository.AddAsync(step.Next, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            // The employee's first contract may have been activated while this event was being handled: both read no profile, and
            // the activation made its version with no unit and no regime. Read again, and fill what that version lacks. (Two
            // transactions that are both still open do not see each other: what is left then is what the repair
            // api/payroll/admin/backfill-profile-orgunit fills, from this very placeholder.)
            var written = await repository.EmployeePayProfiles
                .Where(p => p.EmployeeId == request.EmployeeId && p.Id != step.Next.Id)
                .ToListAsync(cancellationToken);
            if (written.Count == 0 || !FillEmpty(EmployeePayProfileVersioning.PreviousOf(written)!, request))
                return;
            await repository.SaveChangesAsync(cancellationToken);
            return;
        }

        // The profile in force, else the latest: the one a later version starts from.
        var current = EmployeePayProfileVersioning.PreviousOf(profiles)!;
        if (FillEmpty(current, request))
            await repository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Fills the unit and the regime of <paramref name="current"/> where they are empty; never overwrites. True when it changed one.</summary>
    private static bool FillEmpty(EmployeePayProfile current, HandleEmployeeCreatedCommand request)
    {
        var changed = false;
        if (current.OrgUnitId is null && request.OrgUnitId is Guid unit)
        {
            current.OrgUnitId = unit;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(current.RegimeCode) && !string.IsNullOrWhiteSpace(request.PayrollRegimeCode))
        {
            current.RegimeCode = request.PayrollRegimeCode.Trim();
            changed = true;
        }

        return changed;
    }
}

public record HandleTrainingCompletedCommand(Guid EmployeeId, Guid CourseId, decimal Score) : IRequest;

public class HandleTrainingCompletedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleTrainingCompletedCommand>
{
    public async Task Handle(HandleTrainingCompletedCommand request, CancellationToken cancellationToken)
    {
        var config = await repository.GetActiveConfigurationAsync(cancellationToken);
        if (config is null || !config.EnableTrainingPremiums || config.TrainingPremiumAmount <= 0)
            return;

        var marker = $"TrainingCompleted-{request.CourseId}";
        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = config.TrainingPremiumAmount,
            AdjustmentReason = $"علاوة تدريب — {marker} Score={request.Score}"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record HandleMedicalRecommendationIssuedCommand(
    Guid RecommendationId,
    Guid EmployeeId,
    string RecommendationType,
    string Details) : IRequest;

public class HandleMedicalRecommendationIssuedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleMedicalRecommendationIssuedCommand>
{
    public async Task Handle(HandleMedicalRecommendationIssuedCommand request, CancellationToken cancellationToken)
    {
        if (!MedicalCompensationRules.IsCompensationRecommendation(request.RecommendationType))
            return;

        var marker = $"MedicalRecommendation-{request.RecommendationId}";
        var exists = await repository.PayrollAdjustments.AnyAsync(
            a => a.EmployeeId == request.EmployeeId
                 // PLAN-54: only a row the system wrote marks the event as handled; a manual input never suppresses it.
                 && a.Origin == ManualInputOrigin.System
                 && a.AdjustmentReason != null
                 && a.AdjustmentReason.Contains(marker),
            cancellationToken);
        if (exists)
            return;

        var config = await repository.GetActiveConfigurationAsync(cancellationToken);
        var amount = MedicalCompensationRules.ResolveAmount(
            request.Details, config?.MedicalCompensationDefaultAmount ?? 0);

        if (amount == 0)
            return;

        await repository.AddAsync(new PayrollAdjustment
        {
            EmployeeId = request.EmployeeId,
            Amount = amount,
            AdjustmentReason = MedicalCompensationRules.BuildAdjustmentReason(request.RecommendationId, request.Details)
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record MarkPayslipSentCommand(Guid SalaryLineId) : IRequest<(bool Success, string? Error)>;

public class MarkPayslipSentHandler(IPayrollRepository repository)
    : IRequestHandler<MarkPayslipSentCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        MarkPayslipSentCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.PayslipDeliveries
            .FirstOrDefaultAsync(p => p.SalaryLineId == request.SalaryLineId, cancellationToken);

        if (existing is null)
        {
            await repository.AddAsync(new PayslipDelivery
            {
                SalaryLineId = request.SalaryLineId,
                Status = Domain.Enums.PayslipDeliveryStatus.Sent,
                SentAt = DateTime.UtcNow
            }, cancellationToken);
        }
        else
        {
            existing.Status = Domain.Enums.PayslipDeliveryStatus.Sent;
            existing.SentAt = DateTime.UtcNow;
        }

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
