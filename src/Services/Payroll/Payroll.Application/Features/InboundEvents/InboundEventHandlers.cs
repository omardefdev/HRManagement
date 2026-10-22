using HRManagement.MessageContracts.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Payroll.Application.Abstractions;
using Payroll.Application.Options;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
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
/// </remarks>
public class HandleContractActivatedHandler(
    IPayrollRepository repository,
    IOptions<PayrollActivationOptions>? options = null,
    ILogger<HandleContractActivatedHandler>? logger = null)
    : IRequestHandler<HandleContractActivatedCommand>
{
    public const string RuleOutOfRange = "PAY029-RANGE";
    public const string RuleNoSalary = "PAY029-NOSAL";
    public const string OutOfRangeReasonAr = "الراتب خارج حدود الدرجة";
    public const string NoSalaryReasonAr = "لا يوجد راتب أساسي عند تفعيل العقد";
    public const string AlertCategory = "PayProfileActivation";

    private readonly ILogger _logger = logger ?? NullLogger<HandleContractActivatedHandler>.Instance;

    public async Task Handle(HandleContractActivatedCommand request, CancellationToken cancellationToken)
    {
        var grade = await ResolveGradeAsync(request, cancellationToken);
        var salary = request.BaseSalary ?? grade?.BaseSalary;
        var requireSalary = options?.Value.RequireBaseSalaryOnActivation ?? false;
        var baseSalary = salary ?? 0;
        var existing = await repository.GetActiveProfileAsync(request.EmployeeId, cancellationToken);

        if (existing is null)
        {
            await repository.AddAsync(new EmployeePayProfile
            {
                EmployeeId = request.EmployeeId,
                PayGradeId = grade?.Id,
                BaseSalary = baseSalary,
                EffectiveFrom = request.StartDate,
                // PAY-029: with Payroll:RequireBaseSalaryOnActivation an unpriced profile is created inactive.
                IsActive = salary is not null || !requireSalary
            }, cancellationToken);
        }
        else
        {
            await repository.AddAsync(
                EmployeePayProfileRules.CreateHistorySnapshot(existing, $"تفعيل عقد {request.ContractType}"),
                cancellationToken);

            existing.IsActive = false;
            existing.EffectiveTo = request.StartDate;

            await repository.AddAsync(new EmployeePayProfile
            {
                EmployeeId = request.EmployeeId,
                PayGradeId = grade?.Id ?? existing.PayGradeId,
                BaseSalary = baseSalary > 0 ? baseSalary : existing.BaseSalary,
                EffectiveFrom = request.StartDate,
                IsActive = true
            }, cancellationToken);
        }

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
        CancellationToken cancellationToken)
    {
        if (await repository.PaymentHolds.AnyAsync(
                h => h.EmployeeId == request.EmployeeId
                     && h.Status == PaymentHoldStatus.Active
                     && h.HoldType == PaymentHoldType.Manual
                     && h.Reason.StartsWith(ruleId),
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
            Reason = $"{ruleId}: {reasonAr}",
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
                            && d.DeductionType == "AdvanceRecovery")
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

public class HandleEmployeeCreatedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleEmployeeCreatedCommand>
{
    public async Task Handle(HandleEmployeeCreatedCommand request, CancellationToken cancellationToken)
    {
        if (await repository.EmployeePayProfiles.AnyAsync(p => p.EmployeeId == request.EmployeeId, cancellationToken))
            return;

        await repository.AddAsync(new EmployeePayProfile
        {
            EmployeeId = request.EmployeeId,
            OrgUnitId = request.OrgUnitId,
            RegimeCode = request.PayrollRegimeCode,
            BaseSalary = 0,
            EffectiveFrom = DateTime.UtcNow,
            IsActive = false
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
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
