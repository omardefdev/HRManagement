using System.Collections;
using System.Reflection;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-54 (P-6): a draft cloned from a version carries every value that governs pay: every field, every child table, every reference.
/// The entities are read by reflection, in the manner of PLAN-53's copy-forward test: a property that is neither carried, nor named
/// here as deliberately not carried (identity, the cycle's state, the dates the draft takes for itself), fails the test, so a field or
/// a table added later cannot be dropped silently. The old clone copied the brackets only and inherited its source's dates.
/// </summary>
public sealed class Pay054CloneCarriesEverythingTests
{
    private static readonly DateTime NewDate = new(2032, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>What a draft does not take from its source, and why.</summary>
    private static readonly string[] NotCarriedByAVersion =
    [
        // identity and bookkeeping
        nameof(PayrollConfiguration.Id), nameof(PayrollConfiguration.CreatedAt), nameof(PayrollConfiguration.UpdatedAt),
        // the draft's own place in the cycle
        nameof(PayrollConfiguration.VersionNumber), nameof(PayrollConfiguration.PublicationStatus), nameof(PayrollConfiguration.IsActive),
        nameof(PayrollConfiguration.ContentHash), nameof(PayrollConfiguration.HashSchemaVersion),
        nameof(PayrollConfiguration.ApprovedBy), nameof(PayrollConfiguration.ApprovedAt),
        nameof(PayrollConfiguration.PublishedBy), nameof(PayrollConfiguration.PublishedAt),
        nameof(PayrollConfiguration.SupersedesConfigurationId),
        // the dates belong to the version (P-6): the draft takes the date it is given and has no end
        nameof(PayrollConfiguration.EffectiveFrom), nameof(PayrollConfiguration.EffectiveTo),
        // who shaped the source is not who shapes the draft
        nameof(PayrollConfiguration.Editors),
    ];

    private static readonly string[] NotCarriedByARow =
    [
        nameof(TaxBracket.Id), nameof(TaxBracket.CreatedAt), nameof(TaxBracket.UpdatedAt),
        nameof(TaxBracket.PayrollConfigurationId), nameof(TaxBracket.PayrollConfiguration),
        // the draft is the rule set of its own rules, and a rule inside a version is in force
        nameof(DisbursementPriorityRule.RuleSetId), nameof(DisbursementPriorityRule.IsActive),
    ];

    private static IEnumerable<PropertyInfo> Writable(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite);

    private static bool IsRowCollection(PropertyInfo property) =>
        property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>);

    /// <summary>A distinct value for a property that differs from a new object's, so a carried value is told from a default.</summary>
    private static object Sample(PropertyInfo property, int seed, object fresh)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(Guid)) return Guid.Parse($"54000000-0000-4000-8000-{seed:D12}");
        if (type == typeof(string)) return $"v{seed}";
        if (type == typeof(decimal)) return 10m + seed;
        if (type == typeof(int)) return 3 + seed;
        if (type == typeof(bool)) return !(bool)(property.GetValue(fresh) ?? false);
        if (type == typeof(DateTime)) return new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(seed);
        if (type == typeof(DateOnly)) return new DateOnly(2025, 1, 1).AddDays(seed);
        if (type.IsEnum) return Enum.GetValues(type).Cast<object>().First(v => !v.Equals(property.GetValue(fresh)));
        throw new InvalidOperationException(
            $"{property.DeclaringType!.Name}.{property.Name} has a type this test cannot fill ({property.PropertyType.Name}): teach Sample() the type, then decide whether a clone carries it.");
    }

    private static object FilledRow(Type rowType, PayrollConfiguration owner, int seed)
    {
        var row = Activator.CreateInstance(rowType)!;
        foreach (var property in Writable(rowType))
        {
            if (property.PropertyType == typeof(PayrollConfiguration)) continue;
            property.SetValue(row, Sample(property, seed++, Activator.CreateInstance(rowType)!));
        }

        rowType.GetProperty(nameof(TaxBracket.PayrollConfigurationId))!.SetValue(row, owner.Id);
        rowType.GetProperty(nameof(TaxBracket.TenantId))!.SetValue(row, owner.TenantId);
        return row;
    }

    /// <summary>A v2 version with every property set to a distinct value and two rows in every table.</summary>
    private static PayrollConfiguration FullSource()
    {
        var source = new PayrollConfiguration();
        var seed = 1;
        foreach (var property in Writable(typeof(PayrollConfiguration)).Where(p => !IsRowCollection(p)))
            property.SetValue(source, Sample(property, seed++, new PayrollConfiguration()));
        source.HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion;
        source.PublicationStatus = PayrollConfigPublicationStatus.Published;

        foreach (var table in Writable(typeof(PayrollConfiguration)).Where(IsRowCollection))
        {
            var rowType = table.PropertyType.GetGenericArguments()[0];
            var rows = (IList)table.GetValue(source)!;
            rows.Add(FilledRow(rowType, source, seed += 20));
            rows.Add(FilledRow(rowType, source, seed += 20));
        }

        return source;
    }

    [Fact]
    public void A_clone_of_a_v2_version_carries_every_field()
    {
        var source = FullSource();

        var clone = PayrollConfigurationVersioning.CloneAsDraft(source, source.VersionNumber + 1, NewDate);

        foreach (var property in Writable(typeof(PayrollConfiguration)).Where(p => !IsRowCollection(p)))
        {
            if (NotCarriedByAVersion.Contains(property.Name)) continue;
            Assert.True(
                Equals(property.GetValue(source), property.GetValue(clone)),
                $"PayrollConfiguration.{property.Name} is not carried by CloneAsDraft and is not named as deliberately not carried.");
        }
    }

    [Fact]
    public void A_clone_of_a_v2_version_carries_every_row_of_every_table_with_every_value()
    {
        var source = FullSource();

        var clone = PayrollConfigurationVersioning.CloneAsDraft(source, source.VersionNumber + 1, NewDate);

        foreach (var table in Writable(typeof(PayrollConfiguration)).Where(IsRowCollection))
        {
            if (NotCarriedByAVersion.Contains(table.Name)) continue;
            var rowType = table.PropertyType.GetGenericArguments()[0];
            var sourceRows = ((IEnumerable)table.GetValue(source)!).Cast<object>().ToList();
            var cloneRows = ((IEnumerable)table.GetValue(clone)!).Cast<object>().ToList();
            Assert.True(sourceRows.Count == cloneRows.Count, $"{table.Name}: the clone has {cloneRows.Count} rows of {sourceRows.Count}.");

            var carried = Writable(rowType)
                .Where(p => !NotCarriedByARow.Contains(p.Name) && p.Name != nameof(TaxBracket.TenantId))
                .ToList();
            Assert.NotEmpty(carried);
            foreach (var sourceRow in sourceRows)
            {
                Assert.True(
                    cloneRows.Any(cloneRow => carried.All(p => Equals(p.GetValue(sourceRow), p.GetValue(cloneRow)))),
                    $"{table.Name}: a row of the source has no row in the clone that carries all of {string.Join(", ", carried.Select(p => p.Name))}.");
            }

            foreach (var cloneRow in cloneRows)
            {
                Assert.Equal(clone.Id, rowType.GetProperty(nameof(TaxBracket.PayrollConfigurationId))!.GetValue(cloneRow));
                Assert.Equal(source.TenantId, rowType.GetProperty(nameof(TaxBracket.TenantId))!.GetValue(cloneRow));
                Assert.NotEqual(Guid.Empty, rowType.GetProperty(nameof(TaxBracket.Id))!.GetValue(cloneRow));
            }
        }
    }

    [Fact]
    public void A_clone_is_a_v2_draft_with_its_own_date_no_end_and_no_editor()
    {
        var source = FullSource();
        source.EffectiveTo = new DateTime(2030, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var clone = PayrollConfigurationVersioning.CloneAsDraft(source, 7, new DateTime(2032, 3, 1, 15, 30, 0, DateTimeKind.Unspecified));

        Assert.Equal(PayrollConfigPublicationStatus.Draft, clone.PublicationStatus);
        Assert.Equal(PayrollConfiguration.CurrentHashSchemaVersion, clone.HashSchemaVersion);
        Assert.Equal(7, clone.VersionNumber);
        Assert.Equal(NewDate, clone.EffectiveFrom);
        Assert.Equal(DateTimeKind.Utc, clone.EffectiveFrom.Kind);
        Assert.Null(clone.EffectiveTo);
        Assert.Null(clone.ContentHash);
        Assert.False(clone.IsActive);
        Assert.Null(clone.ApprovedBy);
        Assert.Null(clone.PublishedBy);
        Assert.Equal(source.Id, clone.SupersedesConfigurationId);
        Assert.Empty(clone.Editors);
        Assert.NotEqual(source.Id, clone.Id);
    }

    [Fact]
    public void A_clone_of_a_v2_version_has_the_fingerprint_of_its_source_once_it_takes_the_same_date()
    {
        var source = Pay054TestFrameworks.FullV2();

        var clone = PayrollConfigurationVersioning.CloneAsDraft(source, 2, source.EffectiveFrom);

        Assert.Equal(
            PayrollConfigurationHashService.ComputeContentHash(source),
            PayrollConfigurationHashService.ComputeContentHash(clone));
    }

    /// <summary>
    /// P-6: a version older than PLAN-54 holds none of the three tables; its runs read the general ones. Its v2 draft starts from
    /// exactly those rows, each interim with no reference and no exception, so nobody can approve it before classifying them.
    /// </summary>
    [Fact]
    public void A_clone_of_a_v1_version_starts_from_what_a_v1_run_reads_today_unclassified()
    {
        var v1 = PayrollRegimeSeed.CreateLaw50Configuration();
        foreach (var own in PayrollRegimeSeed.CreateDefaultHolidays(v1.Id)) v1.Holidays.Add(own);
        var general = new OfficialHoliday { Date = new DateOnly(2000, 3, 21), NameAr = "عيد الأم", IsRecurring = true };
        var otherVersionsRule = new DisbursementPriorityRule
        {
            Category = "Foreign",
            Priority = 9,
            LegalSource = "x",
            IsActive = true,
            PayrollConfigurationId = Guid.NewGuid(),
        };
        var legacy = new PayrollLegacyReferenceData(
            [
                new DeductionType { Code = "DED-SOC", NameAr = "تأمينات اجتماعية", IsStatutory = true, Percentage = 7m },
                new DeductionType { Code = "DED-FIX", NameAr = "طابع", IsStatutory = true, FixedAmount = 250m },
                new DeductionType { Code = "DED-LOAN", NameAr = "قسط سلفة", IsStatutory = false, FixedAmount = 25_000m },
            ],
            [general],
            [
                new DisbursementPriorityRule { Category = "LowIncome", Priority = 1, MinimumAmount = 250_000m, LegalSource = "تعميم", IsActive = true },
                new DisbursementPriorityRule { Category = "Retired", Priority = 2, LegalSource = "تعميم", IsActive = false },
                otherVersionsRule,
            ]);

        var draft = PayrollConfigurationVersioning.CloneAsDraft(v1, 2, NewDate, legacy: legacy);

        Assert.Equal(PayrollConfiguration.CurrentHashSchemaVersion, draft.HashSchemaVersion);
        Assert.Equal(v1.TaxBrackets.Count, draft.TaxBrackets.Count);

        Assert.Equal(["DED-SOC", "DED-FIX"], draft.StatutoryDeductions.OrderBy(d => d.SortOrder).Select(d => d.Code));
        var soc = draft.StatutoryDeductions.Single(d => d.Code == "DED-SOC");
        Assert.Equal(7m, soc.EmployeePercent);
        Assert.Equal(0m, soc.FixedAmount);
        Assert.Equal(StatutoryDeductionBaseKind.GrossPay, soc.BaseKind);
        Assert.Null(soc.Ceiling);
        Assert.Equal(250m, draft.StatutoryDeductions.Single(d => d.Code == "DED-FIX").FixedAmount);

        Assert.Equal(5, draft.Holidays.Count);
        Assert.Contains(draft.Holidays, h => h.NameAr == "عيد الأم");
        Assert.All(draft.Holidays, h => Assert.Equal(draft.Id, h.PayrollConfigurationId));

        var rule = Assert.Single(draft.PriorityRules);
        Assert.Equal("LowIncome", rule.Category);
        Assert.Equal(draft.Id, rule.PayrollConfigurationId);
        Assert.Equal(draft.Id, rule.RuleSetId);

        Assert.Equal(
            PayrollConfigurationFieldSource.GovernedFields.OrderBy(f => f, StringComparer.Ordinal),
            draft.FieldSources.Select(f => f.FieldName).OrderBy(f => f, StringComparer.Ordinal));

        Assert.All(draft.LegalValues(), value =>
        {
            Assert.Equal(PayrollLegalValueStatus.Interim, value.LegalStatus);
            Assert.Null(value.InterimExceptionId);
        });
        Assert.All(draft.StatutoryDeductions, d => Assert.Null(d.LegalReference));
        Assert.All(draft.FieldSources, f => Assert.Null(f.LegalReference));
    }

    /// <summary>The governed fields are exactly the fields the v1 fingerprint covers, so no field that governs pay lacks a source.</summary>
    [Fact]
    public void The_governed_fields_exist_on_the_version_and_are_all_in_the_fingerprint()
    {
        var canonical = PayrollConfigurationHashService.BuildV2Canonical(Pay054TestFrameworks.FullV2());
        foreach (var field in PayrollConfigurationFieldSource.GovernedFields)
        {
            Assert.NotNull(typeof(PayrollConfiguration).GetProperty(field));
            Assert.Contains($"\"{char.ToLowerInvariant(field[0])}{field[1..]}\":", canonical, StringComparison.Ordinal);
        }

        Assert.Equal(PayrollConfigurationFieldSource.GovernedFields.Count, PayrollConfigurationFieldSource.GovernedFields.Distinct().Count());
    }
}
