using System.Globalization;
using HRManagement.ServiceDefaults.LegalControl;
using Payroll.Application.Abstractions;

namespace Payroll.Infrastructure;

public sealed class PayrollLegalControlGuard(ILegalControlGuard guard, ILegalControlCatalogProvider catalog) : IPayrollLegalControlGuard
{
    public string? ValidatePayrollRelease() => guard.ValidatePayrollRelease();

    public bool IsCapabilityAllowed(string requirementId) => guard.IsCapabilityAllowed(requirementId);

    public bool IsExceptionActive(string? exceptionId, DateOnly asOf) =>
        ExceptionExpiresOn(exceptionId) is { } lastDay && asOf <= lastDay;

    public DateOnly? ExceptionExpiresOn(string? exceptionId)
    {
        if (string.IsNullOrWhiteSpace(exceptionId))
            return null;
        var exception = catalog.GetException(exceptionId.Trim());
        if (exception is null || !string.Equals(exception.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return null;
        return DateOnly.TryParseExact(exception.ExpiresOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastDay)
            ? lastDay
            : null;
    }
}
