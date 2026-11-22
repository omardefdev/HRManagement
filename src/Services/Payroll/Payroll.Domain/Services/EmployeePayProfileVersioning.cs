using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

/// <summary>
/// PLAN-53 (§2): how a pay profile gets its next version. The previous version closes the day before the new one starts (as
/// <see cref="PayrollConfigurationVersioning"/> closes a configuration), so no day belongs to two versions; the new version carries
/// <b>every</b> field of the previous one, and then the change. <c>Iam053ApplierCopyForwardTests</c> reads the entity by reflection: a
/// property added later must be copied here or named in <see cref="DeliberatelyNotCopied"/> or <see cref="SetByTheChange"/>.
/// </summary>
public static class EmployeePayProfileVersioning
{
    /// <summary>The identity and bookkeeping of a row: a new version has its own.</summary>
    public static readonly IReadOnlySet<string> DeliberatelyNotCopied = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(EmployeePayProfile.Id),
        nameof(EmployeePayProfile.CreatedAt),
        nameof(EmployeePayProfile.UpdatedAt),
        // The navigation follows PayGradeId; copying the tracked grade of another version is not a field of the profile.
        nameof(EmployeePayProfile.PayGrade),
    };

    /// <summary>What a new version is given by the change itself: its period, and the salary and grade the change proposes (else the previous ones).</summary>
    public static readonly IReadOnlySet<string> SetByTheChange = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(EmployeePayProfile.EffectiveFrom),
        nameof(EmployeePayProfile.EffectiveTo),
        nameof(EmployeePayProfile.IsActive),
        nameof(EmployeePayProfile.BaseSalary),
        nameof(EmployeePayProfile.PayGradeId),
    };

    /// <summary>A calendar date as the profile stores it: 00:00 UTC of that date, never shifted by a zone (PLAN-67's rule).</summary>
    public static DateTime ToStoredDate(DateOnly date) => new(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The calendar date a stored profile date stands for.</summary>
    public static DateOnly ToCalendarDate(DateTime stored) => DateOnly.FromDateTime(stored);

    /// <summary>The last day of the previous version: the day before the new one starts.</summary>
    public static DateTime ClosingDate(DateOnly newEffectiveFrom) => ToStoredDate(newEffectiveFrom.AddDays(-1));

    /// <summary>
    /// A new version cannot start before the version it replaces: the days between would belong to a version that never held them.
    /// </summary>
    public static bool StartsBefore(DateOnly newEffectiveFrom, EmployeePayProfile previous) =>
        newEffectiveFrom < ToCalendarDate(previous.EffectiveFrom);

    /// <summary>Closes the previous version: inactive, ending the day before <paramref name="newEffectiveFrom"/>.</summary>
    public static void Close(EmployeePayProfile previous, DateOnly newEffectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(previous);
        previous.IsActive = false;
        previous.EffectiveTo = ClosingDate(newEffectiveFrom);
    }

    /// <summary>
    /// The next version: every field of <paramref name="previous"/>, then the period and the proposal. A null proposal keeps the
    /// previous value.
    /// </summary>
    public static EmployeePayProfile NextVersion(
        EmployeePayProfile previous,
        DateOnly effectiveFrom,
        decimal? proposedBaseSalary,
        Guid? proposedPayGradeId)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return new EmployeePayProfile
        {
            // carried from the previous version
            TenantId = previous.TenantId,
            EmployeeId = previous.EmployeeId,
            OrgUnitId = previous.OrgUnitId,
            RegimeCode = previous.RegimeCode,
            BankAccountIban = previous.BankAccountIban,
            BankName = previous.BankName,
            BankAccountVerifiedAt = previous.BankAccountVerifiedAt,
            // set by the change
            BaseSalary = proposedBaseSalary ?? previous.BaseSalary,
            PayGradeId = proposedPayGradeId ?? previous.PayGradeId,
            EffectiveFrom = ToStoredDate(effectiveFrom),
            EffectiveTo = null,
            IsActive = true,
        };
    }
}
