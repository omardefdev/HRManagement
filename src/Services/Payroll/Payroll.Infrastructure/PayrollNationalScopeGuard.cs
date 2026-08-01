using HRManagement.ServiceDefaults.NationalScope;
using Payroll.Application.Abstractions;

namespace Payroll.Infrastructure;

public sealed class PayrollNationalScopeGuard(INationalScopeGuard guard, INationalScopeProvider scope)
    : IPayrollNationalScopeGuard
{
    public string? ValidatePayrollPersonCount(int personCount) =>
        guard.CheckPayrollPersonCount(personCount);

    public string DefaultCurrencyCode => scope.ResolvedCurrencyCode;
}
