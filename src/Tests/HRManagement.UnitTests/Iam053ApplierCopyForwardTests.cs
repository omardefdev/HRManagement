using System.Reflection;
using Payroll.Domain.Entities;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-53 (§2, P-5): the next version of a pay profile carries every field of the previous one. The entity is read by reflection: a
/// property that is neither copied, nor named as deliberately not copied (the id and the row's bookkeeping), nor set by the change
/// itself (the period, the salary, the grade) fails the test, so a field added later cannot be dropped silently as the old upsert
/// dropped the unit, the regime and the bank account. The previous version closes the day before the new one starts.
/// </summary>
public sealed class Iam053ApplierCopyForwardTests
{
    private static readonly DateOnly NewStart = new(2026, 12, 1);

    private static IEnumerable<PropertyInfo> Properties() =>
        typeof(EmployeePayProfile).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite);

    /// <summary>A distinct, non-default value for a property, so a copy is told from a default.</summary>
    private static object Sample(PropertyInfo property, int seed)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(Guid)) return Guid.Parse($"53000000-0000-4000-8000-{seed:D12}");
        if (type == typeof(string)) return $"value-{property.Name}-{seed}";
        if (type == typeof(decimal)) return 1000m + seed;
        if (type == typeof(int)) return 100 + seed;
        if (type == typeof(bool)) return true;
        if (type == typeof(DateTime)) return new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(seed);
        if (type == typeof(DateOnly)) return new DateOnly(2025, 1, 1).AddDays(seed);
        if (type == typeof(PayGrade)) return new PayGrade { GradeCode = $"G{seed}", GradeNameAr = "درجة" };
        throw new InvalidOperationException(
            $"EmployeePayProfile.{property.Name} has a type this test cannot fill ({property.PropertyType.Name}): teach Sample() the type, then decide whether the next version copies it.");
    }

    private static EmployeePayProfile FullPrevious()
    {
        var previous = new EmployeePayProfile();
        var seed = 1;
        foreach (var property in Properties())
            property.SetValue(previous, Sample(property, seed++));
        previous.EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        previous.EffectiveTo = null;
        previous.IsActive = true;
        return previous;
    }

    [Fact]
    public void Every_property_is_copied_or_named_as_not_copied_or_set_by_the_change()
    {
        var previous = FullPrevious();
        var next = EmployeePayProfileVersioning.NextVersion(previous, NewStart, proposedBaseSalary: null, proposedPayGradeId: null);

        var dropped = new List<string>();
        foreach (var property in Properties())
        {
            if (EmployeePayProfileVersioning.DeliberatelyNotCopied.Contains(property.Name)
                || EmployeePayProfileVersioning.SetByTheChange.Contains(property.Name))
                continue;
            if (!Equals(property.GetValue(previous), property.GetValue(next)))
                dropped.Add(property.Name);
        }

        Assert.True(dropped.Count == 0,
            "The next pay profile version drops these fields of the previous one (copy them in EmployeePayProfileVersioning.NextVersion, "
            + "or name them in DeliberatelyNotCopied with the reason): " + string.Join(", ", dropped));
    }

    [Fact]
    public void The_named_sets_hold_only_real_properties_and_do_not_overlap()
    {
        var names = Properties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.All(EmployeePayProfileVersioning.DeliberatelyNotCopied, n => Assert.Contains(n, names));
        Assert.All(EmployeePayProfileVersioning.SetByTheChange, n => Assert.Contains(n, names));
        Assert.Empty(EmployeePayProfileVersioning.DeliberatelyNotCopied.Intersect(EmployeePayProfileVersioning.SetByTheChange));
        // The not-copied set stays the row's identity and bookkeeping: a business field never hides in it. Since PLAN-55 it also names
        // the three obsolete bank columns (the employee's account is EmployeeBankAccount): only a member marked [Obsolete] may join.
        var obsolete = Properties().Where(p => p.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0)
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new[] { "BankAccountIban", "BankAccountVerifiedAt", "BankName" }, obsolete.Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "CreatedAt", "Id", "PayGrade", "UpdatedAt" },
            EmployeePayProfileVersioning.DeliberatelyNotCopied.Where(n => !obsolete.Contains(n)).Order(StringComparer.Ordinal));
        Assert.All(obsolete, n => Assert.Contains(n, EmployeePayProfileVersioning.DeliberatelyNotCopied));
    }

    [Fact]
    public void The_obsolete_bank_columns_are_left_empty_on_the_next_version()
    {
        // PLAN-55: nothing reads them, and the account belongs to the employee, not to a version. Read by reflection: an obsolete
        // member is not referenced by name.
        var previous = FullPrevious();
        var next = EmployeePayProfileVersioning.NextVersion(previous, NewStart, 2500m, null);

        foreach (var name in new[] { "BankAccountIban", "BankName", "BankAccountVerifiedAt" })
        {
            var property = typeof(EmployeePayProfile).GetProperty(name)!;
            Assert.NotNull(property.GetValue(previous));
            Assert.Null(property.GetValue(next));
        }
    }

    [Fact]
    public void The_unit_and_the_regime_reach_the_next_version()
    {
        var previous = FullPrevious();
        var next = EmployeePayProfileVersioning.NextVersion(previous, NewStart, 2500m, null);

        Assert.Equal(previous.TenantId, next.TenantId);
        Assert.Equal(previous.EmployeeId, next.EmployeeId);
        Assert.Equal(previous.OrgUnitId, next.OrgUnitId);
        Assert.Equal(previous.RegimeCode, next.RegimeCode);
        Assert.NotEqual(previous.Id, next.Id);
    }

    [Fact]
    public void The_proposal_is_applied_and_a_missing_part_keeps_the_previous_value()
    {
        var previous = FullPrevious();
        var grade = Guid.NewGuid();

        var salaryOnly = EmployeePayProfileVersioning.NextVersion(previous, NewStart, 2500m, null);
        Assert.Equal(2500m, salaryOnly.BaseSalary);
        Assert.Equal(previous.PayGradeId, salaryOnly.PayGradeId);

        var gradeOnly = EmployeePayProfileVersioning.NextVersion(previous, NewStart, null, grade);
        Assert.Equal(previous.BaseSalary, gradeOnly.BaseSalary);
        Assert.Equal(grade, gradeOnly.PayGradeId);

        var both = EmployeePayProfileVersioning.NextVersion(previous, NewStart, 2600m, grade);
        Assert.Equal(2600m, both.BaseSalary);
        Assert.Equal(grade, both.PayGradeId);
    }

    [Fact]
    public void The_new_version_starts_on_the_calendar_date_at_midnight_utc_and_is_the_active_one()
    {
        var next = EmployeePayProfileVersioning.NextVersion(FullPrevious(), NewStart, null, null);

        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), next.EffectiveFrom);
        Assert.Equal(DateTimeKind.Utc, next.EffectiveFrom.Kind);
        Assert.Null(next.EffectiveTo);
        Assert.True(next.IsActive);
    }

    [Theory]
    [InlineData(2026, 12, 1, 2026, 11, 30)]
    [InlineData(2027, 1, 1, 2026, 12, 31)]   // December to January
    [InlineData(2027, 3, 1, 2027, 2, 28)]    // February
    [InlineData(2028, 3, 1, 2028, 2, 29)]    // a leap February
    public void The_previous_version_closes_the_day_before_so_no_day_is_counted_twice(int y, int m, int d, int cy, int cm, int cd)
    {
        var previous = FullPrevious();
        var start = new DateOnly(y, m, d);

        EmployeePayProfileVersioning.Close(previous, start);
        var next = EmployeePayProfileVersioning.NextVersion(previous, start, null, null);

        Assert.False(previous.IsActive);
        Assert.Equal(new DateTime(cy, cm, cd, 0, 0, 0, DateTimeKind.Utc), previous.EffectiveTo);
        // The two periods touch and do not overlap: the previous one ends the day before the next one starts.
        Assert.Equal(DateOnly.FromDateTime(previous.EffectiveTo!.Value).AddDays(1), DateOnly.FromDateTime(next.EffectiveFrom));
        // The old upsert set EffectiveTo to the new EffectiveFrom, so the boundary day belonged to both versions.
        Assert.NotEqual(next.EffectiveFrom, previous.EffectiveTo);
    }

    [Fact]
    public void A_version_cannot_start_before_the_one_it_replaces()
    {
        var previous = FullPrevious();   // from 2026-01-01
        Assert.True(EmployeePayProfileVersioning.StartsBefore(new DateOnly(2025, 12, 1), previous));
        Assert.False(EmployeePayProfileVersioning.StartsBefore(new DateOnly(2026, 1, 1), previous));
        Assert.False(EmployeePayProfileVersioning.StartsBefore(new DateOnly(2026, 2, 1), previous));
    }

    [Fact]
    public void The_history_snapshot_of_the_closed_version_records_its_last_day()
    {
        var previous = FullPrevious();
        EmployeePayProfileVersioning.Close(previous, NewStart);

        var history = EmployeePayProfileRules.CreateHistorySnapshot(previous, "سبب");

        Assert.Equal(previous.Id, history.SourceProfileId);
        Assert.Equal(previous.BaseSalary, history.BaseSalary);
        Assert.Equal(new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc), history.EffectiveTo);
    }
}
