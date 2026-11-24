using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

/// <summary>
/// PLAN-55 (§3): what a source says about the next version of an employee's pay profile. A null salary or grade keeps the value of
/// the version in force (and is 0 or none when no version is in force); a null unit or regime keeps the employee's.
/// </summary>
public sealed record PayProfileVersionChange(
    DateOnly EffectiveFrom,
    decimal? BaseSalary = null,
    Guid? PayGradeId = null,
    Guid? OrgUnitId = null,
    string? RegimeCode = null,
    bool IsActive = true,
    string HistoryReason = "");

/// <summary>
/// The outcome of <see cref="EmployeePayProfileVersioning.CreateNextVersion"/>: the new version (to be added), the version it starts
/// from (already closed in place when it was open, or null for a first version), and the history snapshot of that closing (to be added).
/// </summary>
public sealed record PayProfileVersionStep(EmployeePayProfile Next, EmployeePayProfile? Previous, EmployeePayProfileHistory? History)
{
    /// <summary>The previous version was in force until this step (its salary and grade were carried).</summary>
    public bool PreviousWasInForce { get; init; }
}

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
        // PLAN-55: the three obsolete bank columns. The employee's account is EmployeeBankAccount, which belongs to the employee and not
        // to a version; nothing reads these, so a new version leaves them empty. (Named as text: an obsolete member is not referenced.)
        "BankAccountIban",
        "BankName",
        "BankAccountVerifiedAt",
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
        // The day before the new version starts; a version that already ends earlier keeps its end (closing never extends a period).
        // A version replaced on its own first day held no day: its end is the day before its start, and no day is counted twice.
        var closing = ClosingDate(newEffectiveFrom);
        previous.EffectiveTo = previous.EffectiveTo is DateTime end && end < closing ? end : closing;
    }

    /// <summary>
    /// The version a new one starts from: the active profile, else the employee's latest profile, active or not (PLAN-55: the
    /// inactive, unpriced profile <c>IEmployeeCreated</c> wrote carries the unit and the regime). Null when the employee has none.
    /// </summary>
    public static EmployeePayProfile? PreviousOf(IEnumerable<EmployeePayProfile> profilesOfEmployee)
    {
        ArgumentNullException.ThrowIfNull(profilesOfEmployee);
        var profiles = profilesOfEmployee as IReadOnlyCollection<EmployeePayProfile> ?? profilesOfEmployee.ToList();
        return profiles.FirstOrDefault(p => p.IsActive)
               ?? profiles.OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.CreatedAt).ThenBy(p => p.Id).FirstOrDefault();
    }

    /// <summary>
    /// A profile that was never in force and never closed: the placeholder <c>IEmployeeCreated</c> writes, or the unpriced profile an
    /// activation without a salary leaves inactive. It never held a day, so a version replaces it whatever its date.
    /// </summary>
    public static bool IsPlaceholder(EmployeePayProfile profile) => !profile.IsActive && profile.EffectiveTo is null;

    /// <summary>
    /// Whether a version starting on <paramref name="newEffectiveFrom"/> would take days that belong to <paramref name="previous"/>:
    /// before the start of a version in force, or on or before the last day of a closed one. Never for a placeholder.
    /// </summary>
    public static bool StartsInside(DateOnly newEffectiveFrom, EmployeePayProfile previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (previous.IsActive)
            return StartsBefore(newEffectiveFrom, previous);
        return previous.EffectiveTo is DateTime end && newEffectiveFrom <= ToCalendarDate(end);
    }

    /// <summary>
    /// PLAN-55 (§3): <b>the only place a new pay profile is made</b> (<c>PayProfileWritersTests</c> refuses another). It starts from
    /// <see cref="PreviousOf"/>; with no profile at all it makes the first one.
    /// <list type="bullet">
    /// <item>The unit and the regime are carried from the previous version, in force or not, unless the source gives new ones.</item>
    /// <item>Every other field is carried as PLAN-53's copy-forward carries it (<see cref="NextVersion"/>), with one difference: the
    /// salary and the grade are carried only from a version <b>in force</b>. Those of a placeholder or of a closed version ended with
    /// it, so the new version takes what the source gives, else 0 and no grade.</item>
    /// <item>A previous version that is open (in force, or a placeholder) is closed the day before the new one starts — never on the
    /// start date, so no day is in two versions — and its history snapshot is made. A version that was already closed keeps its end.</item>
    /// <item>Dates are calendar dates stored at 00:00 UTC (<see cref="ToStoredDate"/>); the caller takes «today» from the payroll
    /// calendar, never from the system clock.</item>
    /// </list>
    /// The caller checks <see cref="StartsInside"/> first when it must refuse, adds <see cref="PayProfileVersionStep.Next"/> and
    /// <see cref="PayProfileVersionStep.History"/> to its unit of work, and saves once.
    /// </summary>
    public static PayProfileVersionStep CreateNextVersion(
        Guid employeeId, IEnumerable<EmployeePayProfile> profilesOfEmployee, PayProfileVersionChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var regime = string.IsNullOrWhiteSpace(change.RegimeCode) ? null : change.RegimeCode.Trim();
        var previous = PreviousOf(profilesOfEmployee);
        if (previous is null)
        {
            var first = new EmployeePayProfile
            {
                EmployeeId = employeeId,
                OrgUnitId = change.OrgUnitId,
                RegimeCode = regime,
                BaseSalary = change.BaseSalary ?? 0m,
                PayGradeId = change.PayGradeId,
                EffectiveFrom = ToStoredDate(change.EffectiveFrom),
                IsActive = change.IsActive,
            };
            return new PayProfileVersionStep(first, null, null);
        }

        var wasInForce = previous.IsActive;
        // The end the previous version was planned to have, read before it is closed; only a version in force has a plan to carry.
        var plannedEnd = wasInForce ? previous.EffectiveTo : null;
        EmployeePayProfileHistory? history = null;
        if (wasInForce || previous.EffectiveTo is null)
        {
            Close(previous, change.EffectiveFrom);
            history = EmployeePayProfileRules.CreateHistorySnapshot(previous, change.HistoryReason);
        }

        var next = NextVersion(previous, change.EffectiveFrom, change.BaseSalary, change.PayGradeId, plannedEnd);
        if (!wasInForce)
        {
            next.BaseSalary = change.BaseSalary ?? 0m;
            next.PayGradeId = change.PayGradeId;
        }

        if (change.OrgUnitId is Guid unit)
            next.OrgUnitId = unit;
        if (regime is not null)
            next.RegimeCode = regime;
        next.IsActive = change.IsActive;
        return new PayProfileVersionStep(next, previous, history) { PreviousWasInForce = wasInForce };
    }

    /// <summary>
    /// The next version: every field of <paramref name="previous"/>, then the period and the proposal. A null proposal keeps the
    /// previous value. Called by <see cref="CreateNextVersion"/> only (and by the tests of the copy-forward).
    /// </summary>
    public static EmployeePayProfile NextVersion(
        EmployeePayProfile previous,
        DateOnly effectiveFrom,
        decimal? proposedBaseSalary,
        Guid? proposedPayGradeId,
        DateTime? plannedEnd = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var start = ToStoredDate(effectiveFrom);
        return new EmployeePayProfile
        {
            // carried from the previous version
            TenantId = previous.TenantId,
            EmployeeId = previous.EmployeeId,
            OrgUnitId = previous.OrgUnitId,
            RegimeCode = previous.RegimeCode,
            // set by the change
            BaseSalary = proposedBaseSalary ?? previous.BaseSalary,
            PayGradeId = proposedPayGradeId ?? previous.PayGradeId,
            EffectiveFrom = start,
            // The end the previous version was planned to have (read before it was closed) still holds when it lies on or after the
            // new start; a change never makes a profile that was to end open-ended.
            EffectiveTo = plannedEnd is DateTime end && end >= start ? end : null,
            IsActive = true,
        };
    }
}
