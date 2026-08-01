using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services.Engine;

namespace Payroll.Application.Features.PayrollRuns;

public record CreateCorrectionRunCommand(
    Guid SourceRunId,
    DateTime RunDate) : IRequest<(bool Success, Guid? CorrectionRunId, string? Error)>;

public class CreateCorrectionRunHandler(
    IPayrollRepository repository,
    IPayrollEngine engine)
    : IRequestHandler<CreateCorrectionRunCommand, (bool Success, Guid? CorrectionRunId, string? Error)>
{
    public async Task<(bool Success, Guid? CorrectionRunId, string? Error)> Handle(
        CreateCorrectionRunCommand request,
        CancellationToken cancellationToken)
    {
        var source = await repository.GetPayrollRunAsync(request.SourceRunId, includeLines: true, cancellationToken);
        if (source is null) return (false, null, "التشغيل المرجعي غير موجود.");
        if (source.Status != PayrollRunStatus.Posted)
            return (false, null, "التصحيح الرجعي يتطلب تشغيلاً مرحّلاً.");
        if (!source.PayrollConfigurationId.HasValue || string.IsNullOrEmpty(source.ConfigContentHash))
            return (false, null, "التشغيل المرجعي بلا نسخة إعدادات مثبتة؛ أعد حساب/ترحيل المصدر وفق PAY-002.");

        var config = await repository.GetConfigurationByIdAsync(source.PayrollConfigurationId.Value, cancellationToken);
        if (config is null)
            return (false, null, "نسخة الإعدادات المثبتة على التشغيل المرجعي غير موجودة.");
        if (!string.Equals(source.ConfigContentHash, config.ContentHash, StringComparison.OrdinalIgnoreCase))
            return (false, null, "بصمة إعدادات التشغيل المرجعي لا تطابق النسخة القانونية.");

        if (config.MaxRetroCorrectionMonths > 0)
        {
            var months = ((request.RunDate.Year - source.PeriodStart.Year) * 12)
                         + request.RunDate.Month - source.PeriodStart.Month;
            if (months > config.MaxRetroCorrectionMonths)
                return (false, null, $"تجاوز حد التصحيح الرجعي ({config.MaxRetroCorrectionMonths} شهر).");
        }

        var correction = new PayrollRun
        {
            PeriodStart = source.PeriodStart,
            PeriodEnd = source.PeriodEnd,
            RunDate = request.RunDate,
            Status = PayrollRunStatus.Draft,
            RunType = PayrollRunType.Correction,
            SourceRunId = source.Id,
            PayrollConfigurationId = source.PayrollConfigurationId,
            ConfigContentHash = source.ConfigContentHash
        };
        await repository.AddAsync(correction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        var periodStart = DateOnly.FromDateTime(correction.PeriodStart);
        var periodEnd = DateOnly.FromDateTime(correction.PeriodEnd);
        var holidays = await repository.OfficialHolidays
            .Where(h => h.PayrollConfigurationId == null || h.PayrollConfigurationId == config.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var extraHolidays = PayrollRunCalculationSupport.BuildHolidaySet(periodStart, periodEnd, holidays);
        var deductionTypes = await repository.DeductionTypes.AsNoTracking().ToListAsync(cancellationToken);

        foreach (var sourceLine in source.SalaryLines)
        {
            var profile = await repository.GetActiveProfileAsync(sourceLine.EmployeeId, cancellationToken);
            if (profile is null) continue;

            if (await repository.SalaryLines.AnyAsync(
                    l => l.PayrollRunId == correction.Id && l.EmployeeId == sourceLine.EmployeeId, cancellationToken))
                continue;

            var newAdjustments = await repository.PayrollAdjustments
                .Where(a => a.EmployeeId == sourceLine.EmployeeId && a.PayrollRunId == null)
                .ToListAsync(cancellationToken);

            var deltaGross = newAdjustments.Sum(a => a.Amount);
            var deltaNet = deltaGross;

            var package = PayrollEmployeeCalculationUnit.Build(
                engine, correction, profile, profile.PayGrade, config, extraHolidays,
                Array.Empty<AllowanceRecord>(),
                Array.Empty<DeductionRecord>(),
                deductionTypes,
                newAdjustments,
                Array.Empty<AbsenceDeduction>(),
                correctionDeltaGross: deltaGross,
                correctionDeltaNet: deltaNet);

            var (saved, _) = await repository.SaveEmployeePackageAtomicAsync(correction.Id, package, cancellationToken);
            if (saved)
                correction.SalaryLines.Add(package.Line);
        }

        var totalNet = correction.SalaryLines.Sum(l => l.NetPay);
        if (correction.SalaryLines.Count == 0)
        {
            // Reload from DB if collection not tracked
            var lines = await repository.SalaryLines
                .Where(l => l.PayrollRunId == correction.Id)
                .ToListAsync(cancellationToken);
            totalNet = lines.Sum(l => l.NetPay);
        }

        correction.MarkCalculated(totalNet);
        correction.ValidatedAtCalculate = true;
        correction.ResultsFrozen = true;
        await repository.SaveChangesAsync(cancellationToken);

        return (true, correction.Id, null);
    }
}
