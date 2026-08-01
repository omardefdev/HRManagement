using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

public class Pay003ReconciliationTests
{
    [Fact]
    public void Validate_Fails_WhenLineHasNoComponents()
    {
        var lineId = Guid.NewGuid();
        var run = new PayrollRun { TotalNetPay = 900m };
        var lines = new List<SalaryLine>
        {
            new()
            {
                Id = lineId,
                EmployeeId = Guid.NewGuid(),
                GrossPay = 1000m,
                TotalDeductions = 100m,
                NetPay = 900m
            }
        };

        var (ok, err) = PayrollRunReconciliation.ValidateTotals(
            run, lines, new Dictionary<Guid, List<SalaryLineComponent>>());
        Assert.False(ok);
        Assert.Contains("بلا مكوّنات", err!);
    }

    [Fact]
    public void Validate_Fails_OnMismatchedTotals()
    {
        var lineId = Guid.NewGuid();
        var run = new PayrollRun { TotalNetPay = 500m };
        var lines = new List<SalaryLine>
        {
            new()
            {
                Id = lineId,
                EmployeeId = Guid.NewGuid(),
                GrossPay = 1000m,
                TotalDeductions = 100m,
                NetPay = 900m
            }
        };
        var comps = new Dictionary<Guid, List<SalaryLineComponent>>
        {
            [lineId] =
            [
                new SalaryLineComponent
                {
                    SalaryLineId = lineId,
                    ComponentType = SalaryComponentType.BaseSalary,
                    ComponentName = "أساسي",
                    Amount = 900m
                }
            ]
        };

        var (ok, err) = PayrollRunReconciliation.ValidateTotals(run, lines, comps);
        Assert.False(ok);
        Assert.Contains("TotalNetPay", err!);
    }

    [Fact]
    public void Validate_Fails_WhenEmployeeCountMismatch()
    {
        var run = new PayrollRun { TotalNetPay = 0m };
        var (ok, err) = PayrollRunReconciliation.ValidateTotals(
            run, Array.Empty<SalaryLine>(), new Dictionary<Guid, List<SalaryLineComponent>>(), expectedEmployeeCount: 3);
        Assert.False(ok);
        Assert.Contains("لقطة المدخلات", err!);
    }

    [Fact]
    public void ProfileVersion_Changes_WhenSalaryChanges()
    {
        var profile = new EmployeePayProfile
        {
            EmployeeId = Guid.NewGuid(),
            BaseSalary = 1000m,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true
        };
        var v1 = Payroll.Application.Services.PayrollEmployeeCalculationUnit.ComputeProfileVersion(profile);
        profile.BaseSalary = 2000m;
        var v2 = Payroll.Application.Services.PayrollEmployeeCalculationUnit.ComputeProfileVersion(profile);
        Assert.NotEqual(v1, v2);
    }
}
