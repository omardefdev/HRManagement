using Payroll.Application.Services;
using Payroll.Domain.Entities;

namespace HRManagement.UnitTests;

public class Perf001FingerprintTests
{
    [Fact]
    public void Fingerprint_Is_Stable_For_Same_Inputs()
    {
        var emp = Guid.NewGuid();
        var allowance = new AllowanceRecord
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EmployeeId = emp,
            AllowanceTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Amount = 100.5m,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var a = PayrollInputFingerprint.Compute([allowance], [], [], []);
        var b = PayrollInputFingerprint.Compute([allowance], [], [], []);
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void Fingerprint_Changes_When_Amount_Changes()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var type = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var emp = Guid.NewGuid();
        var a = PayrollInputFingerprint.Compute(
            [new AllowanceRecord { Id = id, EmployeeId = emp, AllowanceTypeId = type, Amount = 10m, EffectiveFrom = DateTime.UtcNow }],
            [], [], []);
        var b = PayrollInputFingerprint.Compute(
            [new AllowanceRecord { Id = id, EmployeeId = emp, AllowanceTypeId = type, Amount = 11m, EffectiveFrom = DateTime.UtcNow }],
            [], [], []);
        Assert.NotEqual(a, b);
    }
}
