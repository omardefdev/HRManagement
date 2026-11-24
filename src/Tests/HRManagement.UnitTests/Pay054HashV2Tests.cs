using System.Reflection;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-54 (§2, P-7): the two fingerprints. v1 is frozen: the fingerprints stored on the versions and runs that exist today are
/// reproduced to the character. v2 covers every value that governs pay: the fields, the effective date, the four tables inside the
/// version, the field sources, and every reference, status and exception; it does not move with row order, decimal scale or the
/// record of the version's editors.
/// </summary>
public sealed class Pay054HashV2Tests
{
    /// <summary>The fingerprints of the three seeded versions, as the demo server stores them since they were first seeded.</summary>
    [Theory]
    [InlineData("LAW_50_2004", "9d0288237dcc9dceee8d735b9dccd560c4e787d4ce0b99a6646c6d1ad0f8de45")]
    [InlineData("LAW_53_2021", "6a3fa3da3f9cf2ffcd042d99d4a61c37987006180c4a2f8849f8a9e259fd80ea")]
    [InlineData("CIVIL_SERVICE_2026", "a5de27a1ab1f5820a3a0c9cd475adce9504b31c7c3d321064128a8962c94c8a7")]
    public void V1_fingerprint_of_a_seeded_version_is_the_one_stored_before_this_plan(string code, string expected)
    {
        var config = code switch
        {
            "LAW_50_2004" => PayrollRegimeSeed.CreateLaw50Configuration(),
            "LAW_53_2021" => PayrollRegimeSeed.CreateLaw53Configuration(),
            _ => PayrollRegimeSeed.CreateCivilServiceConfiguration(),
        };

        Assert.Equal(PayrollConfiguration.LegacyHashSchemaVersion, config.HashSchemaVersion);
        Assert.True(config.IsLegacyFramework);
        Assert.Equal(expected, config.ContentHash);
        Assert.Equal(expected, PayrollConfigurationHashService.ComputeContentHash(config));
    }

    [Fact]
    public void V1_fingerprint_ignores_everything_this_plan_added()
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration();
        var before = PayrollConfigurationHashService.ComputeContentHash(config);

        config.EffectiveFrom = new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        config.StatutoryDeductions.Add(Deduction("DED-X", 9m));
        config.Holidays.Add(Holiday(new DateOnly(2000, 3, 8), "عطلة"));
        config.PriorityRules.Add(Priority("General", 1));
        config.FieldSources.Add(Field(nameof(PayrollConfiguration.TaxExemptionAmount)));
        foreach (var bracket in config.TaxBrackets)
        {
            bracket.LegalReference = "LD-TAX-BRACKETS";
            bracket.LegalStatus = PayrollLegalValueStatus.Signed;
        }

        Assert.Equal(before, PayrollConfigurationHashService.ComputeContentHash(config));
    }

    [Fact]
    public void V2_fingerprint_differs_from_v1_for_the_same_fields_and_brackets()
    {
        var v1 = PayrollRegimeSeed.CreateLaw50Configuration();
        var v2 = PayrollRegimeSeed.CreateLaw50Configuration();
        v2.HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion;

        Assert.NotEqual(
            PayrollConfigurationHashService.ComputeContentHash(v1),
            PayrollConfigurationHashService.ComputeContentHash(v2));
    }

    public static TheoryData<string, Action<PayrollConfiguration>> ContentChanges() => new()
    {
        { "effective date", c => c.EffectiveFrom = c.EffectiveFrom.AddMonths(1) },
        { "code", c => c.Code += "_X" },
        { "regime", c => c.RegimeCode = "LAW_53_2021" },
        { "tax exemption", c => c.TaxExemptionAmount += 1m },
        { "daily divisor", c => c.DailyRateDivisor = 26 },
        { "rounding", c => c.RoundingMode = PayrollRoundingMode.ToEven },
        { "medical compensation", c => c.MedicalCompensationDefaultAmount += 5m },
        { "bracket rate", c => c.TaxBrackets.First().RatePercent += 1m },
        { "bracket limit", c => c.TaxBrackets.First().UpperLimit += 1m },
        { "bracket reference", c => c.TaxBrackets.First().LegalReference = "LD-OTHER" },
        { "bracket status", c => c.TaxBrackets.First().LegalStatus = PayrollLegalValueStatus.Signed },
        { "bracket exception", c => c.TaxBrackets.First().InterimExceptionId = "EX-OTHER" },
        { "bracket removed", c => c.TaxBrackets.Remove(c.TaxBrackets.Last()) },
        { "deduction employee percent", c => c.StatutoryDeductions.First().EmployeePercent += 0.5m },
        { "deduction employer percent", c => c.StatutoryDeductions.First().EmployerPercent += 0.5m },
        { "deduction fixed amount", c => c.StatutoryDeductions.First().FixedAmount += 10m },
        { "deduction base", c => c.StatutoryDeductions.First().BaseKind = StatutoryDeductionBaseKind.BaseSalary },
        { "deduction ceiling", c => c.StatutoryDeductions.First().Ceiling = 500_000m },
        { "deduction insurance body", c => c.StatutoryDeductions.First().InsuranceBody = "GOIP" },
        { "deduction name", c => c.StatutoryDeductions.First().NameAr += " (معدّل)" },
        { "deduction code", c => c.StatutoryDeductions.First().Code += "-2" },
        { "deduction order", c => c.StatutoryDeductions.First().SortOrder = 99 },
        { "deduction reference", c => c.StatutoryDeductions.First().LegalReference = "مرجع آخر" },
        { "deduction status", c => c.StatutoryDeductions.First().LegalStatus = PayrollLegalValueStatus.Signed },
        { "deduction exception", c => c.StatutoryDeductions.First().InterimExceptionId = "EX-OTHER" },
        { "deduction added", c => c.StatutoryDeductions.Add(Deduction("DED-NEW", 1m)) },
        { "holiday date", c => c.Holidays.First().Date = c.Holidays.First().Date.AddDays(1) },
        { "holiday name", c => c.Holidays.First().NameAr += " الجديدة" },
        { "holiday recurrence", c => c.Holidays.First().IsRecurring = !c.Holidays.First().IsRecurring },
        { "holiday reference", c => c.Holidays.First().LegalReference = "مرجع آخر" },
        { "holiday removed", c => c.Holidays.Remove(c.Holidays.First()) },
        { "priority minimum", c => c.PriorityRules.First().MinimumAmount += 1m },
        { "priority order", c => c.PriorityRules.First().Priority += 10 },
        { "priority category", c => c.PriorityRules.First().Category += "X" },
        { "priority reference", c => c.PriorityRules.First().LegalSource = "مصدر آخر" },
        { "priority status", c => c.PriorityRules.First().LegalStatus = PayrollLegalValueStatus.Signed },
        { "priority exception", c => c.PriorityRules.First().InterimExceptionId = "EX-OTHER" },
        { "field source reference", c => c.FieldSources.First().LegalReference = "LD-OTHER" },
        { "field source status", c => c.FieldSources.First().LegalStatus = PayrollLegalValueStatus.Signed },
        { "field source exception", c => c.FieldSources.First().InterimExceptionId = "EX-OTHER" },
        { "field source removed", c => c.FieldSources.Remove(c.FieldSources.First()) },
    };

    [Theory]
    [MemberData(nameof(ContentChanges))]
    public void V2_fingerprint_moves_with_every_value_that_governs_pay(string what, Action<PayrollConfiguration> change)
    {
        var config = Pay054TestFrameworks.FullV2();
        var before = PayrollConfigurationHashService.ComputeContentHash(config);

        change(config);

        Assert.True(before != PayrollConfigurationHashService.ComputeContentHash(config), $"the fingerprint did not move with: {what}");
    }

    [Fact]
    public void V2_fingerprint_does_not_move_with_row_order_decimal_scale_editors_or_bookkeeping()
    {
        var config = Pay054TestFrameworks.FullV2();
        var before = PayrollConfigurationHashService.ComputeContentHash(config);

        config.TaxBrackets = config.TaxBrackets.Reverse().ToList();
        config.StatutoryDeductions = config.StatutoryDeductions.Reverse().ToList();
        config.Holidays = config.Holidays.Reverse().ToList();
        config.PriorityRules = config.PriorityRules.Reverse().ToList();
        config.FieldSources = config.FieldSources.Reverse().ToList();
        // 5 and 5.00 are one value: the database gives back two decimals whatever was written.
        foreach (var d in config.StatutoryDeductions) d.EmployeePercent = decimal.Parse(d.EmployeePercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        config.TaxExemptionAmount += 0.00m;
        config.RecordEditor(Guid.NewGuid(), PayrollFrameworkEditKind.Edited, DateTime.UtcNow);
        config.NameAr += " (اسم العرض)";
        config.IsActive = !config.IsActive;
        config.VersionNumber += 5;
        config.PublicationStatus = PayrollConfigPublicationStatus.Approved;
        config.ApprovedBy = Guid.NewGuid();
        config.Id = Guid.NewGuid();
        foreach (var b in config.TaxBrackets) b.Id = Guid.NewGuid();

        Assert.Equal(before, PayrollConfigurationHashService.ComputeContentHash(config));
    }

    /// <summary>
    /// A field added to a version or to one of its tables must be decided: inside the fingerprint, or named here as not content. The
    /// test reads the canonical text by reflection, so a new property fails it until someone decides.
    /// </summary>
    [Fact]
    public void Every_property_of_a_version_and_of_its_tables_is_in_the_v2_fingerprint_or_named_as_not_content()
    {
        var canonical = PayrollConfigurationHashService.BuildV2Canonical(Pay054TestFrameworks.FullV2());

        AssertCovered(typeof(PayrollConfiguration), canonical,
            // Identity, bookkeeping and the cycle's state: who and when, never what.
            nameof(PayrollConfiguration.Id), nameof(PayrollConfiguration.TenantId), nameof(PayrollConfiguration.CreatedAt),
            nameof(PayrollConfiguration.UpdatedAt), nameof(PayrollConfiguration.NameAr), nameof(PayrollConfiguration.IsActive),
            nameof(PayrollConfiguration.VersionNumber), nameof(PayrollConfiguration.PublicationStatus),
            nameof(PayrollConfiguration.ContentHash), nameof(PayrollConfiguration.EffectiveTo),
            nameof(PayrollConfiguration.ApprovedBy), nameof(PayrollConfiguration.ApprovedAt),
            nameof(PayrollConfiguration.PublishedBy), nameof(PayrollConfiguration.PublishedAt),
            nameof(PayrollConfiguration.SupersedesConfigurationId), nameof(PayrollConfiguration.HashSchemaVersion),
            nameof(PayrollConfiguration.Editors));
        AssertCovered(typeof(TaxBracket), canonical, Bookkeeping(nameof(TaxBracket.PayrollConfigurationId), nameof(TaxBracket.PayrollConfiguration)));
        AssertCovered(typeof(StatutoryDeductionRule), canonical,
            Bookkeeping(nameof(StatutoryDeductionRule.PayrollConfigurationId), nameof(StatutoryDeductionRule.PayrollConfiguration)));
        AssertCovered(typeof(OfficialHoliday), canonical,
            Bookkeeping(nameof(OfficialHoliday.PayrollConfigurationId), nameof(OfficialHoliday.PayrollConfiguration)));
        AssertCovered(typeof(PayrollConfigurationFieldSource), canonical,
            Bookkeeping(nameof(PayrollConfigurationFieldSource.PayrollConfigurationId), nameof(PayrollConfigurationFieldSource.PayrollConfiguration)));
        // A rule inside a version is in force through its version (IsActive is the general table's switch and is stored off for a
        // version's rule), the version is its rule set, and LegalSource is written as the rule's legal reference.
        AssertCovered(typeof(DisbursementPriorityRule), canonical,
            Bookkeeping(nameof(DisbursementPriorityRule.PayrollConfigurationId), nameof(DisbursementPriorityRule.PayrollConfiguration),
                nameof(DisbursementPriorityRule.RuleSetId), nameof(DisbursementPriorityRule.IsActive), nameof(DisbursementPriorityRule.LegalSource)));
        // (That a rule's LegalSource moves the fingerprint is the «priority reference» row of ContentChanges.)
    }

    private static string[] Bookkeeping(params string[] more) =>
        [nameof(TaxBracket.Id), nameof(TaxBracket.TenantId), nameof(TaxBracket.CreatedAt), nameof(TaxBracket.UpdatedAt), .. more];

    private static void AssertCovered(Type type, string canonical, params string[] notContent)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite))
        {
            if (notContent.Contains(property.Name)) continue;
            var json = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            Assert.True(
                canonical.Contains($"\"{json}\":", StringComparison.Ordinal),
                $"{type.Name}.{property.Name} is neither in fingerprint v2 nor named as not content: a value that governs pay must be hashed.");
        }
    }

    internal static StatutoryDeductionRule Deduction(string code, decimal percent) => new()
    {
        Code = code,
        NameAr = "خصم نظامي " + code,
        EmployeePercent = percent,
        LegalReference = "مرجع " + code,
        LegalStatus = PayrollLegalValueStatus.Interim,
        InterimExceptionId = "EX-STR2-LAW50-UNCERTIFIED",
    };

    internal static OfficialHoliday Holiday(DateOnly date, string name) => new()
    {
        Date = date,
        NameAr = name,
        IsRecurring = true,
        LegalReference = "LD-OFFICIAL-HOLIDAYS",
        LegalStatus = PayrollLegalValueStatus.Interim,
        InterimExceptionId = "EX-STR2-LAW50-UNCERTIFIED",
    };

    internal static DisbursementPriorityRule Priority(string category, int priority) => new()
    {
        Category = category,
        Priority = priority,
        MinimumAmount = 1000m * priority,
        LegalSource = "مصدر قاعدة الأولوية",
        LegalStatus = PayrollLegalValueStatus.Interim,
        InterimExceptionId = "EX-STR2-LAW50-UNCERTIFIED",
    };

    internal static PayrollConfigurationFieldSource Field(string name) => new()
    {
        FieldName = name,
        LegalReference = "LD-" + name.ToUpperInvariant(),
        LegalStatus = PayrollLegalValueStatus.Interim,
        InterimExceptionId = "EX-STR2-LAW50-UNCERTIFIED",
    };
}

/// <summary>PLAN-54: framework versions the Pay054 tests share.</summary>
internal static class Pay054TestFrameworks
{
    /// <summary>A v2 draft with every table filled and every value classified.</summary>
    public static PayrollConfiguration FullV2(string code = "LAW_50_2004_T54")
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration(isActive: false);
        config.Code = code;
        config.HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion;
        config.PublicationStatus = PayrollConfigPublicationStatus.Draft;
        config.ContentHash = null;
        config.ApprovedAt = null;
        config.PublishedAt = null;
        config.EffectiveFrom = new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        config.MedicalCompensationDefaultAmount = 15m;

        foreach (var bracket in config.TaxBrackets)
        {
            bracket.PayrollConfigurationId = config.Id;
            bracket.LegalReference = "LD-TAX-BRACKETS";
            bracket.LegalStatus = PayrollLegalValueStatus.Interim;
            bracket.InterimExceptionId = "EX-STR2-TAX-INTERIM";
        }

        var order = 0;
        foreach (var (dedCode, percent) in new[] { ("DED-TAX", 5m), ("DED-SOC", 7m), ("DED-HEALTH", 2m), ("DED-PENS", 3m) })
        {
            var rule = Pay054HashV2Tests.Deduction(dedCode, percent);
            rule.PayrollConfigurationId = config.Id;
            rule.SortOrder = order++;
            rule.EmployerPercent = dedCode == "DED-SOC" ? 14m : 0m;
            rule.InsuranceBody = dedCode is "DED-SOC" or "DED-HEALTH" ? "GOSI" : null;
            rule.FixedAmount = dedCode == "DED-PENS" ? 100m : 0m;
            rule.Ceiling = dedCode == "DED-SOC" ? 900_000m : null;
            config.StatutoryDeductions.Add(rule);
        }

        foreach (var holiday in PayrollRegimeSeed.CreateDefaultHolidays(config.Id))
        {
            holiday.LegalReference = "LD-OFFICIAL-HOLIDAYS";
            holiday.LegalStatus = PayrollLegalValueStatus.Interim;
            holiday.InterimExceptionId = "EX-STR2-LAW50-UNCERTIFIED";
            config.Holidays.Add(holiday);
        }

        foreach (var (category, priority) in new[] { ("LowIncome", 1), ("FamilySupport", 2), ("General", 3) })
        {
            var rule = Pay054HashV2Tests.Priority(category, priority);
            rule.PayrollConfigurationId = config.Id;
            rule.RuleSetId = config.Id;
            config.PriorityRules.Add(rule);
        }

        foreach (var field in PayrollConfigurationFieldSource.GovernedFields)
        {
            var source = Pay054HashV2Tests.Field(field);
            source.PayrollConfigurationId = config.Id;
            config.FieldSources.Add(source);
        }

        return config;
    }
}
