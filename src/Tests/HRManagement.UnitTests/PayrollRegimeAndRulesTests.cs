using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;
using Payroll.Domain.Services.Engine.Rules;

namespace HRManagement.UnitTests;

public class PayrollRegimeAndRulesTests
{
    [Fact]
    public void PayrollRegimeSeed_Creates_Both_Regimes()
    {
        var law50 = PayrollRegimeSeed.CreateLaw50Configuration();
        var civil = PayrollRegimeSeed.CreateCivilServiceConfiguration();

        Assert.Equal(LegalRegimeCodes.Law50_2004, law50.RegimeCode);
        Assert.Equal(LegalRegimeCodes.CivilService2026, civil.RegimeCode);
        Assert.Equal(5, law50.TaxBrackets.Count);
        Assert.Equal(5, civil.TaxBrackets.Count);
    }

    [Fact]
    public void EndOfServiceCalculation_Uses_Config_Rates()
    {
        var config = new PayrollConfiguration
        {
            EosGratuityRateFirstPeriod = 0.4m,
            EosGratuityFirstYearsThreshold = 3,
            EosGratuityRateAfterThreshold = 0.8m
        };

        var gratuity = EndOfServiceCalculation.CalculateGratuity(10_000m, 4m, config);
        Assert.Equal(10_000m * 0.4m * 3m + 10_000m * 0.8m * 1m, gratuity);
    }

    [Fact]
    public void StatutoryDeductionRule_Applies_Percentage()
    {
        var context = BuildContext(statutoryTypes:
        [
            new DeductionType { NameAr = "تأمين", IsStatutory = true, Percentage = 5m }
        ]);
        var result = new PayrollCalculationResult
        {
            Line = new SalaryLine { PayrollRunId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            GrossPay = 10_000m
        };

        new StatutoryDeductionRule().Apply(context, result);
        Assert.Equal(500m, result.TotalDeductions);
    }

    [Fact]
    public void AllowanceRule_Sums_Allowances()
    {
        var context = BuildContext(allowances:
        [
            new AllowanceRecord { Amount = 100m, AllowanceType = new CompensationType { NameAr = "بدل" } },
            new AllowanceRecord { Amount = 200m, AllowanceType = new CompensationType { NameAr = "بدل2" } }
        ]);
        var result = new PayrollCalculationResult
        {
            Line = new SalaryLine { PayrollRunId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() }
        };

        new AllowanceRule().Apply(context, result);
        Assert.Equal(300m, result.GrossPay);
    }

    [Fact]
    public void TaxRule_Applies_Exemption_From_Config()
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration();
        config.TaxExemptionAmount = 9_000m;
        config.TaxBrackets =
        [
            new TaxBracket { UpperLimit = decimal.MaxValue, RatePercent = 10m, SortOrder = 0 }
        ];
        var context = BuildContext(config: config);

        var result = new PayrollCalculationResult
        {
            Line = new SalaryLine { PayrollRunId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            GrossPay = 10_000m
        };

        new TaxRule().Apply(context, result);
        Assert.Equal(100m, result.TotalDeductions);
    }

    private static PayrollCalculationContext BuildContext(
        PayrollConfiguration? config = null,
        IReadOnlyList<AllowanceRecord>? allowances = null,
        IReadOnlyList<DeductionType>? statutoryTypes = null) => new()
        {
            PayrollRunId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            WorkingDaysInPeriod = 22,
            EmployeeWorkingDays = 22,
            MonthlyBaseSalary = 10_000m,
            Allowances = allowances ?? [],
            Deductions = [],
            StatutoryDeductionTypes = statutoryTypes ?? [],
            Adjustments = [],
            AbsenceDeductions = [],
            Configuration = config ?? PayrollRegimeSeed.CreateLaw50Configuration(),
            ExtraHolidays = new HashSet<DateOnly>()
        };
}
