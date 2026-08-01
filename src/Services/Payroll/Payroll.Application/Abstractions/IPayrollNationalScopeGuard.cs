namespace Payroll.Application.Abstractions;

/// <summary>STR-001: payroll window ceiling against the active national envelope.</summary>
public interface IPayrollNationalScopeGuard
{
    string? ValidatePayrollPersonCount(int personCount);
    string DefaultCurrencyCode { get; }
}
