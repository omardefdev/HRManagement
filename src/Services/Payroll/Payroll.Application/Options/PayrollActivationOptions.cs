namespace Payroll.Application.Options;

/// <summary>PAY-029: pay profile creation on IContractActivated (<c>Payroll</c> section).</summary>
public sealed class PayrollActivationOptions
{
    public const string SectionName = "Payroll";

    /// <summary>
    /// <c>Payroll:RequireBaseSalaryOnActivation</c> — when no salary arrives and no grade resolves, create the profile
    /// inactive (default false: keep today's active 0-salary profile so the FUN-001 journey keeps working; both cases
    /// raise a payment hold for review).
    /// </summary>
    public bool RequireBaseSalaryOnActivation { get; set; }
}
