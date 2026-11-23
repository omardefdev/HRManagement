using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayrollConfigurations;

/// <summary>What a run's page and its provenance say about the framework version it is pinned to (PLAN-54, §3).</summary>
public sealed record PayrollRunFrameworkNote(
    string Code,
    int VersionNumber,
    int HashSchemaVersion,
    bool IsLegacy,
    string? InterimIndicator,
    IReadOnlyList<string> InterimExceptionIds)
{
    public const string LegacyNote = "إطار أقدم من حزم الإطار القانوني (قبل PLAN-54): خصوماته النظامية وعطله العامة من الجداول العامة، ولا مرجع قانوني مسجّل لقيمه";
}

/// <summary>
/// An exception a version in force cites, with its last day and how far it is. <see cref="Citation"/> is the exception's number in
/// the STR-002 control catalog: a legal citation a reader looks up, shown as it is.
/// </summary>
public sealed record PayrollExceptionExpiryWarning(string Citation, DateOnly? ExpiresOn, int? DaysLeft, IReadOnlyList<string> Frameworks)
{
    public bool Expired => DaysLeft is null or < 0;
}

/// <summary>PLAN-54 (§3): the indicators of interim frameworks: on a run, and on the dashboard before an exception expires.</summary>
public static class PayrollFrameworkIndicators
{
    /// <summary>The dashboard warns from this many days before an exception's last day.</summary>
    public const int WarningDays = 60;

    public static async Task<PayrollRunFrameworkNote?> ForRunAsync(IPayrollRepository repository, Guid? configurationId, CancellationToken ct)
    {
        if (configurationId is not Guid id)
            return null;
        var version = await repository.GetConfigurationByIdAsync(id, ct);
        return version is null
            ? null
            : new PayrollRunFrameworkNote(
                version.Code,
                version.VersionNumber,
                version.HashSchemaVersion,
                version.IsLegacyFramework,
                PayrollInterimGate.InterimIndicator(version),
                PayrollFrameworkGovernance.CitedExceptionIds(version));
    }

    /// <summary>
    /// The exceptions cited by a published v2 version that end within <see cref="WarningDays"/> days, have ended, or are not in the
    /// catalog: after its last day a version that cites it is neither published nor calculated.
    /// </summary>
    public static async Task<IReadOnlyList<PayrollExceptionExpiryWarning>> ExpiringAsync(
        IPayrollRepository repository,
        IPayrollLegalControlGuard guard,
        IPayrollCalendar calendar,
        CancellationToken ct)
    {
        // In force: a published version, and a superseded one that still governs today (its last day has not passed).
        var todayStart = calendar.Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var inForce = await repository.PayrollConfigurations.AsNoTracking()
            .Where(c => (c.PublicationStatus == PayrollConfigPublicationStatus.Published
                         || (c.PublicationStatus == PayrollConfigPublicationStatus.Superseded && c.EffectiveTo >= todayStart))
                        && c.HashSchemaVersion >= Domain.Entities.PayrollConfiguration.CurrentHashSchemaVersion)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var byException = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var id in inForce)
        {
            var version = await repository.GetConfigurationByIdAsync(id, ct);
            if (version is null) continue;
            foreach (var exception in PayrollFrameworkGovernance.CitedExceptionIds(version))
            {
                if (!byException.TryGetValue(exception, out var frameworks))
                    byException[exception] = frameworks = [];
                frameworks.Add(FormattableString.Invariant($"{version.Code} v{version.VersionNumber}"));
            }
        }

        var today = calendar.Today;
        var warnings = new List<PayrollExceptionExpiryWarning>();
        foreach (var (exception, frameworks) in byException)
        {
            var lastDay = guard.ExceptionExpiresOn(exception);
            int? daysLeft = lastDay is { } day ? day.DayNumber - today.DayNumber : null;
            if (daysLeft is null or <= WarningDays)
                warnings.Add(new PayrollExceptionExpiryWarning(exception, lastDay, daysLeft, frameworks));
        }

        return warnings;
    }
}
