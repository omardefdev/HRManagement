using HRManagement.MessageContracts.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
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

public class HandleContractActivatedHandler(IPayrollRepository repository)
    : IRequestHandler<HandleContractActivatedCommand>
{
    public async Task Handle(HandleContractActivatedCommand request, CancellationToken cancellationToken)
    {
        PayGrade? grade = null;
        if (!string.IsNullOrEmpty(request.PayGradeCode))
            grade = await repository.PayGrades
                .FirstOrDefaultAsync(g => g.GradeCode == request.PayGradeCode, cancellationToken);

        var baseSalary = request.BaseSalary ?? grade?.BaseSalary ?? 0;
        var existing = await repository.GetActiveProfileAsync(request.EmployeeId, cancellationToken);

        if (existing is null)
        {
            await repository.AddAsync(new EmployeePayProfile
            {
                EmployeeId = request.EmployeeId,
                PayGradeId = grade?.Id,
                BaseSalary = baseSalary,
                EffectiveFrom = request.StartDate,
                IsActive = true
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

        await repository.SaveChangesAsync(cancellationToken);
    }
}

public record HandleSeparationCompletedCommand(
    Guid EmployeeId,
    string SeparationType,
    DateTime EffectiveDate,
    Guid? SeparationRecordId,
    int RemainingLeaveDays = 0) : IRequest;

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

            var config = await repository.GetActiveConfigurationAsync(cancellationToken);
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
}

public record HandleTransferExecutedCommand(
    Guid EmployeeId,
    Guid FromOrgUnitId,
    Guid ToOrgUnitId,
    DateTime ExecutedAt,
    decimal? SalaryChange,
    decimal? AllowanceChange) : IRequest;

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

public record HandleEmployeeCreatedCommand(Guid EmployeeId, Guid? OrgUnitId = null) : IRequest;

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
