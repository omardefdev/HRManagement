using Payroll.Application.Features.PayrollConfigurations;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;
using Payroll.Infrastructure.Data;
using H = HRManagement.UnitTests.Pay054FrameworkHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-54 (§2, P-7): the engine on a version of fingerprint v2 reads only the version: its statutory deductions with their base and
/// ceiling, and no built-in bracket. On a version older than PLAN-54 it computes as before. And the identity proof of D-PAY-1: the
/// interim pack, imported, pays every employee exactly what the seeded version pays with the seeded general tables, unit for unit.
/// </summary>
public sealed class Pay054EngineV2Tests
{
    private static readonly IPayrollEngine Engine = PayrollEngine.CreateDefaultPipeline();

    private static PayrollCalculationContext Context(
        PayrollConfiguration config,
        decimal baseSalary,
        IReadOnlyList<DeductionType>? generalTypes = null,
        decimal allowance = 0m,
        int workingDays = 22,
        int employeeDays = 22,
        decimal adHocDeduction = 0m) => new()
        {
            PayrollRunId = Guid.Parse("54000000-0000-4000-8000-0000000000f1"),
            EmployeeId = Guid.Parse("54000000-0000-4000-8000-0000000000e1"),
            PeriodStart = new DateOnly(2026, 10, 1),
            PeriodEnd = new DateOnly(2026, 10, 31),
            WorkingDaysInPeriod = workingDays,
            EmployeeWorkingDays = employeeDays,
            MonthlyBaseSalary = baseSalary,
            Allowances = allowance == 0m
                ? []
                : [new AllowanceRecord { Amount = allowance, AllowanceType = new CompensationType { Code = "ALL", NameAr = "بدل طبيعة عمل" } }],
            Deductions = adHocDeduction == 0m ? [] : [new DeductionRecord { DeductionType = "Other", Amount = adHocDeduction }],
            StatutoryDeductionTypes = generalTypes ?? [],
            Adjustments = [],
            AbsenceDeductions = [],
            Configuration = config,
            ExtraHolidays = new HashSet<DateOnly>(),
        };

    private static List<(SalaryComponentType Type, string Name, decimal Amount)> Lines(PayrollCalculationResult result) =>
        result.Components.Select(c => (c.ComponentType, c.ComponentName, c.Amount)).ToList();

    private static PayrollConfiguration V2(params StatutoryDeductionRule[] rules)
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration();
        config.HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion;
        foreach (var rule in rules) config.StatutoryDeductions.Add(rule);
        return config;
    }

    private static StatutoryDeductionRule Rule(
        string code,
        decimal percent,
        decimal fixedAmount = 0m,
        StatutoryDeductionBaseKind baseKind = StatutoryDeductionBaseKind.GrossPay,
        decimal? ceiling = null,
        int order = 0) => new()
        {
            Code = code,
            NameAr = "نظامي " + code,
            EmployeePercent = percent,
            FixedAmount = fixedAmount,
            BaseKind = baseKind,
            Ceiling = ceiling,
            SortOrder = order,
        };

    /// <summary>The four statutory deduction types of the Demo seed, read from the seeder itself.</summary>
    private static List<DeductionType> DemoSeedDeductionTypes()
    {
        var definitions = (Array)typeof(DemoPayrollSeeder)
            .GetField("DeductionDefs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        return definitions.Cast<(string Code, string NameAr, bool Statutory, decimal? Pct, decimal? Fixed)>()
            .Select(d => new DeductionType { Code = d.Code, NameAr = d.NameAr, IsStatutory = d.Statutory, Percentage = d.Pct, FixedAmount = d.Fixed })
            .ToList();
    }

    // ------------------------------------------------------------------ the v2 rule

    [Fact]
    public void A_v2_statutory_deduction_is_a_percentage_of_gross_pay_plus_a_fixed_amount_in_the_versions_order()
    {
        var config = V2(Rule("B", 7m, order: 1), Rule("A", 2.5m, fixedAmount: 150m, order: 0));

        var result = Engine.Calculate(Context(config, baseSalary: 100_000m, allowance: 20_000m));

        var deductions = result.Components.Where(c => c.ComponentType == SalaryComponentType.Deduction).ToList();
        Assert.Equal(["نظامي A", "نظامي B"], deductions.Select(d => d.ComponentName));
        Assert.Equal(-(120_000m * 2.5m / 100m + 150m), deductions[0].Amount);
        Assert.Equal(-(120_000m * 7m / 100m), deductions[1].Amount);
        Assert.Equal(config.StatutoryDeductions.Single(r => r.Code == "A").Id, deductions[0].ReferenceId);
    }

    [Fact]
    public void A_v2_statutory_deduction_on_the_base_salary_ignores_allowances_and_follows_proration()
    {
        var config = V2(Rule("PENS", 3m, baseKind: StatutoryDeductionBaseKind.BaseSalary));

        var full = Engine.Calculate(Context(config, baseSalary: 100_000m, allowance: 50_000m));
        var half = Engine.Calculate(Context(config, baseSalary: 100_000m, allowance: 50_000m, workingDays: 22, employeeDays: 11));

        Assert.Equal(-3_000m, full.Components.Single(c => c.ComponentName == "نظامي PENS").Amount);
        Assert.Equal(-1_500m, half.Components.Single(c => c.ComponentName == "نظامي PENS").Amount);
    }

    [Theory]
    [InlineData(100_000, 80_000, 5_600)]     // above the ceiling: 7% of the ceiling
    [InlineData(60_000, 80_000, 4_200)]      // below it: 7% of the base
    [InlineData(80_000, 80_000, 5_600)]
    public void A_ceiling_caps_the_base_before_the_percentage(int gross, int ceiling, int expected)
    {
        var config = V2(Rule("SOC", 7m, ceiling: ceiling));

        var result = Engine.Calculate(Context(config, baseSalary: gross));

        Assert.Equal(-expected, result.Components.Single(c => c.ComponentName == "نظامي SOC").Amount);
    }

    [Fact]
    public void A_v2_rule_that_deducts_nothing_writes_no_line_and_the_amount_is_rounded_as_the_version_says()
    {
        var config = V2(Rule("ZERO", 0m), Rule("ODD", 3.33m));

        var result = Engine.Calculate(Context(config, baseSalary: 33_333m));

        Assert.DoesNotContain(result.Components, c => c.ComponentName == "نظامي ZERO");
        Assert.Equal(-config.RoundMoney(33_333m * 3.33m / 100m), result.Components.Single(c => c.ComponentName == "نظامي ODD").Amount);
    }

    [Fact]
    public void A_v2_calculation_ignores_the_general_deduction_types_whatever_they_say()
    {
        var config = V2(Rule("SOC", 7m));
        var clean = Engine.Calculate(Context(config, baseSalary: 100_000m));
        var poisoned = new List<DeductionType>
        {
            new() { Code = "DED-SOC", NameAr = "تأمينات اجتماعية", IsStatutory = true, Percentage = 70m },
            new() { Code = "DED-NEW", NameAr = "اقتطاع أضيف بعد التثبيت", IsStatutory = true, FixedAmount = 9_999m },
        };

        var result = Engine.Calculate(Context(config, baseSalary: 100_000m, generalTypes: poisoned));

        Assert.Equal(Lines(clean), Lines(result));
        Assert.Equal((clean.GrossPay, clean.TotalDeductions, clean.NetPay), (result.GrossPay, result.TotalDeductions, result.NetPay));
    }

    [Fact]
    public void A_v1_calculation_reads_the_general_deduction_types_exactly_as_before_and_ignores_rows_inside_the_version()
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration();
        config.StatutoryDeductions.Add(Rule("IGNORED", 50m));
        var types = DemoSeedDeductionTypes();

        var result = Engine.Calculate(Context(config, baseSalary: 100_000m, generalTypes: types));

        var deductions = result.Components.Where(c => c.ComponentType == SalaryComponentType.Deduction).Select(c => (c.ComponentName, c.Amount)).ToList();
        Assert.Equal(
            [("ضريبة الدخل", -5_000m), ("تأمينات اجتماعية", -7_000m), ("تأمين صحي", -2_000m), ("اشتراك تقاعد", -3_000m)],
            deductions);
        Assert.Equal(100_000m, result.GrossPay);
    }

    // ------------------------------------------------------------------ no built-in bracket on a v2 path

    [Fact]
    public void A_v2_version_without_brackets_is_taxed_by_no_built_in_bracket_and_a_v1_version_keeps_its_fallback()
    {
        var v1 = PayrollRegimeSeed.CreateLaw50Configuration();
        v1.TaxBrackets.Clear();
        var v2 = V2();
        v2.TaxBrackets.Clear();

        var legacy = Engine.Calculate(Context(v1, baseSalary: 100_000m));
        var current = Engine.Calculate(Context(v2, baseSalary: 100_000m));

        Assert.Contains(legacy.Components, c => c.ComponentType == SalaryComponentType.Tax);       // PayrollEngine.DefaultBrackets
        Assert.DoesNotContain(current.Components, c => c.ComponentType == SalaryComponentType.Tax);
        Assert.Equal(100_000m, current.NetPay);
    }

    /// <summary>
    /// P-7 asks first whether any v1 version anywhere has no brackets. None of the seeds does: each seeded regime is built with its
    /// five brackets (and the demo server's three versions hold five each: the baseline's picture). The fallback is reached only by
    /// test fixtures that build a bare <c>new PayrollConfiguration()</c>; it stays on the legacy path for them.
    /// </summary>
    [Fact]
    public void Every_seeded_version_has_brackets()
    {
        Assert.All(
            new[] { PayrollRegimeSeed.CreateLaw50Configuration(), PayrollRegimeSeed.CreateLaw53Configuration(), PayrollRegimeSeed.CreateCivilServiceConfiguration() },
            c => Assert.Equal(5, c.TaxBrackets.Count));
    }

    // ------------------------------------------------------------------ D-PAY-1: the identity proof

    public static TheoryData<int, int, int, int, int> IdentityCases() => new()
    {
        // base salary, allowance, working days, the employee's days, an ad hoc deduction
        { 4_500, 0, 22, 22, 0 },
        { 12_000, 1_500, 22, 22, 0 },
        { 33_333, 777, 21, 21, 250 },
        { 100_000, 20_000, 22, 22, 0 },
        { 100_000, 20_000, 22, 9, 1_000 },          // prorated
        { 250_000, 0, 23, 23, 0 },
        { 1_234_567, 89_012, 22, 17, 3_456 },
        { 9_999_999, 0, 22, 22, 0 },
    };

    [Theory]
    [MemberData(nameof(IdentityCases))]
    public async Task The_imported_interim_pack_pays_exactly_what_the_seeded_version_pays_with_the_seeded_general_tables(
        int baseSalary, int allowance, int workingDays, int employeeDays, int adHoc)
    {
        // The seeded version (v1) with the Demo seed's deduction types.
        var seeded = PayrollRegimeSeed.CreateLaw50Configuration();
        var types = DemoSeedDeductionTypes();

        // The interim pack, imported through the product's importer.
        using var h = new H();
        var imported = await h.ImportAsync();
        Assert.True(imported.Success, imported.Error);
        var fromPack = await h.VersionAsync(imported.FrameworkId!.Value);
        Assert.False(fromPack.IsLegacyFramework);

        var before = Engine.Calculate(Context(seeded, baseSalary, types, allowance, workingDays, employeeDays, adHoc));
        var after = Engine.Calculate(Context(fromPack, baseSalary, generalTypes: [], allowance, workingDays, employeeDays, adHoc));

        Assert.Equal(Lines(before), Lines(after));                       // every line: type, name, amount, in the same order
        Assert.Equal(before.GrossPay, after.GrossPay);
        Assert.Equal(before.TotalDeductions, after.TotalDeductions);
        Assert.Equal(before.NetPay, after.NetPay);
        Assert.Equal((before.Line.GrossPay, before.Line.TotalDeductions, before.Line.NetPay), (after.Line.GrossPay, after.Line.TotalDeductions, after.Line.NetPay));
    }

    [Fact]
    public async Task The_imported_interim_pack_counts_the_same_working_days_as_the_seeded_version_with_its_holidays()
    {
        var seeded = PayrollRegimeSeed.CreateLaw50Configuration();
        var seededHolidays = PayrollRegimeSeed.CreateDefaultHolidays(seeded.Id).ToList();
        using var h = new H();
        var fromPack = await h.VersionAsync((await h.ImportAsync()).FrameworkId!.Value);

        for (var month = 1; month <= 12; month++)
        {
            var start = new DateOnly(2026, month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            var before = Payroll.Application.Services.PayrollRunCalculationSupport.BuildHolidaySet(start, end, seededHolidays);
            var after = Payroll.Application.Services.PayrollRunCalculationSupport.BuildHolidaySet(start, end, fromPack.Holidays);
            Assert.Equal(before.Order(), after.Order());
            Assert.Equal(
                PayrollPeriodRules.CountWorkingDays(start, end, seeded, before),
                PayrollPeriodRules.CountWorkingDays(start, end, fromPack, after));
        }
    }

    /// <summary>
    /// Recorded for D-PAY-1 and PAY-E, not changed here: the interim values withhold income tax twice, once as the statutory
    /// deduction <c>DED-TAX</c> (5% of gross) and once through the brackets. The pack reproduces it because the seeds do it today.
    /// </summary>
    [Fact]
    public async Task The_interim_values_tax_income_twice_as_the_seeds_do_today()
    {
        using var h = new H();
        var fromPack = await h.VersionAsync((await h.ImportAsync()).FrameworkId!.Value);

        var result = Engine.Calculate(Context(fromPack, baseSalary: 100_000m));

        Assert.Equal(-5_000m, result.Components.Single(c => c.ComponentType == SalaryComponentType.Deduction && c.ComponentName == "ضريبة الدخل").Amount);
        Assert.True(result.Components.Single(c => c.ComponentType == SalaryComponentType.Tax && c.ComponentName == "ضريبة الدخل").Amount < 0);
    }
}
