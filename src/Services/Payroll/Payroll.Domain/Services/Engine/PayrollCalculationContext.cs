using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Services.Engine;

public sealed class PayrollCalculationContext
{
    public required Guid PayrollRunId { get; init; }
    public required Guid EmployeeId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required int WorkingDaysInPeriod { get; init; }
    public required int EmployeeWorkingDays { get; init; }
    public required decimal MonthlyBaseSalary { get; init; }
    public required IReadOnlyList<AllowanceRecord> Allowances { get; init; }
    public required IReadOnlyList<DeductionRecord> Deductions { get; init; }
    public required IReadOnlyList<DeductionType> StatutoryDeductionTypes { get; init; }
    public required IReadOnlyList<PayrollAdjustment> Adjustments { get; init; }
    public required IReadOnlyList<AbsenceDeduction> AbsenceDeductions { get; init; }
    public required PayrollConfiguration Configuration { get; init; }
    public required IReadOnlySet<DateOnly> ExtraHolidays { get; init; }
    public PayrollRunType RunType { get; init; } = PayrollRunType.Regular;
    public decimal? CorrectionDeltaGross { get; init; }
    public decimal? CorrectionDeltaNet { get; init; }
}

public sealed class PayrollCalculationResult
{
    public SalaryLine Line { get; init; } = null!;
    public List<SalaryLineComponent> Components { get; init; } = new();
    public decimal GrossPay { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }

    /// <summary>
    /// Ids of the <see cref="DeductionRecord"/> rows this calculation actually charged.
    /// Persistence marks only these as applied. Marking every loaded row instead consumed
    /// future advance installments that were never deducted from anyone's pay.
    /// </summary>
    public HashSet<Guid> ConsumedDeductionIds { get; } = new();

    /// <summary>Ids of the <see cref="AbsenceDeduction"/> rows this calculation actually charged.</summary>
    public HashSet<Guid> ConsumedAbsenceIds { get; } = new();

    /// <summary>Ids of the <see cref="PayrollAdjustment"/> rows this calculation actually applied.</summary>
    public HashSet<Guid> ConsumedAdjustmentIds { get; } = new();
}
