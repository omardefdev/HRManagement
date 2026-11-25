using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-55: test data for the employee's bank account. A test that needs a run to pay somewhere plants an account in force, as two
/// people would have left it (a registrar and a different verifier, with a reference); the run then pins it at its snapshot. The
/// numbers are test IBANs whose check digits are computed from the employee's id: never a real account.
/// </summary>
internal static class Pay055TestAccounts
{
    public static readonly Guid Registrar = Guid.Parse("55000000-0000-4000-8000-0000000000f1");
    public static readonly Guid Verifier = Guid.Parse("55000000-0000-4000-8000-0000000000f2");

    public static string Iban(Guid employeeId, int variant = 0)
    {
        var bban = $"{variant:D2}{employeeId:N}".ToUpperInvariant()[..24];
        return "SY" + BankAccountNumberValidator.ComputeIbanCheckDigits("SY", bban) + bban;
    }

    public static EmployeeBankAccount Active(Guid employeeId, Guid? tenantId = null, int variant = 0) => new()
    {
        TenantId = tenantId ?? Guid.Empty,
        EmployeeId = employeeId,
        AccountKind = BankAccountKind.Iban,
        AccountNumber = Iban(employeeId, variant),
        BankNameAr = "مصرف الاختبار",
        Status = BankAccountStatus.Active,
        CreatedBy = Registrar,
        VerifiedBy = Verifier,
        VerifiedAt = DateTime.UtcNow,
        VerificationReference = "كتاب المصرف (اختبار)",
    };
}
