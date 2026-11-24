namespace Payroll.Application.Abstractions;

/// <summary>STR-002: payroll release gate against the legal control catalog.</summary>
public interface IPayrollLegalControlGuard
{
    /// <summary>Returns error when critical payroll/tax controls are not releaseable.</summary>
    string? ValidatePayrollRelease();

    /// <summary>False for Blocked-Legal capabilities (e.g. MoF certified brackets).</summary>
    bool IsCapabilityAllowed(string requirementId);

    /// <summary>
    /// PLAN-54 (§3): whether a STR-002 exception allows an interim value on <paramref name="asOf"/> (a calendar date of the payroll
    /// calendar, Damascus): the catalog holds it, its status is <c>Active</c>, and <paramref name="asOf"/> is not after its
    /// <c>expiresOn</c> (the exception's last day). An exception the catalog does not hold, or one without a readable date, is not
    /// active. The shared <c>ILegalControlCatalogProvider.IsExceptionActive(id)</c> reads the status only and keeps its meaning: the
    /// services' start-up gate uses it.
    /// </summary>
    bool IsExceptionActive(string? exceptionId, DateOnly asOf);

    /// <summary>The last day of an exception the catalog holds with status <c>Active</c>; null when it holds none such, or no date.</summary>
    DateOnly? ExceptionExpiresOn(string? exceptionId);
}
