using HR.Core.Infrastructure.Integration;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-027 (E2): test double for <see cref="IPayrollGradeLookup"/>. The default answer accepts any salary so
/// tests that are not about offer terms keep working; set <see cref="Result"/> to drive a scenario.
/// </summary>
internal sealed class FakePayrollGradeLookup : IPayrollGradeLookup
{
    public static PayrollGradeLookupResult Permissive() =>
        new PayrollGradeLookupResult.Found(0m, 1_000_000_000m, 0m, "درجة اختبار");

    public PayrollGradeLookupResult Result { get; set; } = Permissive();

    public List<(string GradeCode, DateOnly AsOf)> Calls { get; } = [];

    public Task<PayrollGradeLookupResult> GetAsync(string gradeCode, DateOnly asOf, CancellationToken ct = default)
    {
        Calls.Add((gradeCode, asOf));
        return Task.FromResult(Result);
    }
}
