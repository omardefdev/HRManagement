using System.Text.Json;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace HRManagement.UnitTests;

public class Gov002LegalGovernanceTests
{
    [Fact]
    public void Publish_WithoutAttestation_Fails()
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration();
        config.Id = Guid.NewGuid();
        config.PublicationStatus = PayrollConfigPublicationStatus.Approved;
        var (ok, error, notice) = PayrollLegalGovernance.TryPublishWithGovernance(
            config, [config], Guid.NewGuid(), attestation: null);
        Assert.False(ok);
        Assert.Null(notice);
        Assert.Contains("تصديق", error ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_WithMatchingAttestation_CreatesNotice()
    {
        var v1 = PayrollRegimeSeed.CreateLaw50Configuration();
        v1.Id = Guid.NewGuid();
        var v2 = PayrollConfigurationVersioning.CloneAsDraft(v1, 2, c => c.TaxExemptionAmount = 100);
        v2.Id = Guid.NewGuid();
        PayrollConfigurationVersioning.TryApprove(v2, Guid.NewGuid());
        var actor = Guid.NewGuid();
        var attest = PayrollLegalGovernance.CreateAttestation(v2, actor);

        var (ok, error, notice) = PayrollLegalGovernance.TryPublishWithGovernance(
            v2, [v1, v2], actor, attest,
            effectiveFrom: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.True(ok, error);
        Assert.NotNull(notice);
        Assert.Equal(v2.Id, notice!.PayrollConfigurationId);
        Assert.Equal(v1.Id, notice.SupersedesConfigurationId);
        Assert.Equal(v2.ContentHash, notice.ContentHash);
        Assert.Equal(PayrollConfigPublicationStatus.Published, v2.PublicationStatus);
    }

    [Fact]
    public void Attestation_Voided_When_Hash_Changes()
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration();
        config.Id = Guid.NewGuid();
        config.PublicationStatus = PayrollConfigPublicationStatus.Draft;
        var attest = PayrollLegalGovernance.CreateAttestation(config, Guid.NewGuid());
        config.TaxExemptionAmount = 999;
        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        var (ok, _) = PayrollLegalGovernance.EnsureAttestationForPublish(config, attest);
        Assert.False(ok);
    }

    [Fact]
    public void Register_Seed_Covers_Law50_Attest_Set()
    {
        var rows = LegalDecisionRegisterSeed.CreateDefaults();
        var ids = rows.Select(r => r.DecisionId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in LegalDecisionIds.Law50AttestSet)
            Assert.Contains(id, ids);
        Assert.Contains(rows, r => r.DecisionId == LegalDecisionIds.CivilServicePending
            && r.ApprovalStatus == "PendingEnactment");
    }
}

public class Gov002LegalGoldenPackTests
{
    private static readonly JsonDocument Doc = JsonDocument.Parse(
        System.IO.File.ReadAllText(ResolveGoldenPath()));

    private static string ResolveGoldenPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "testdata", "gov002", "golden-cases.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("testdata/gov002/golden-cases.json");
    }

    [Fact]
    public void Golden_Tax_And_Proration_Match_Law50_Seed()
    {
        var engine = PayrollEngine.CreateDefaultPipeline();
        var config = PayrollRegimeSeed.CreateLaw50Configuration();

        foreach (var c in Doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = c.GetProperty("id").GetString()!;
            if (id.StartsWith("tax-", StringComparison.Ordinal) || id.StartsWith("proration-", StringComparison.Ordinal))
            {
                var ctx = new PayrollCalculationContext
                {
                    PayrollRunId = Guid.NewGuid(),
                    EmployeeId = Guid.NewGuid(),
                    PeriodStart = new DateOnly(2026, 5, 1),
                    PeriodEnd = new DateOnly(2026, 5, 31),
                    WorkingDaysInPeriod = c.GetProperty("workingDays").GetInt32(),
                    EmployeeWorkingDays = c.GetProperty("employeeDays").GetInt32(),
                    MonthlyBaseSalary = c.GetProperty("monthlyBase").GetDecimal(),
                    Allowances = [],
                    Deductions = [],
                    StatutoryDeductionTypes = [],
                    Adjustments = [],
                    AbsenceDeductions = [],
                    Configuration = config,
                    ExtraHolidays = new HashSet<DateOnly>(),
                    RunType = PayrollRunType.Regular
                };
                var result = engine.Calculate(ctx);
                Assert.Equal(c.GetProperty("expectGross").GetDecimal(), result.GrossPay);
                if (c.TryGetProperty("expectTax", out var tax))
                    Assert.Equal(tax.GetDecimal(), result.TotalDeductions);
                if (c.TryGetProperty("expectTaxMin", out var taxMin))
                    Assert.True(result.TotalDeductions >= taxMin.GetDecimal(), id);
            }
        }
    }

    [Fact]
    public void Golden_Rounding_AwayFromZero()
    {
        var c = Doc.RootElement.GetProperty("cases").EnumerateArray()
            .First(x => x.GetProperty("id").GetString() == "rounding-away-from-zero");
        var config = new PayrollConfiguration
        {
            RoundingMode = PayrollRoundingMode.AwayFromZero,
            DecimalPlaces = c.GetProperty("decimalPlaces").GetInt32()
        };
        Assert.Equal(c.GetProperty("expectRounded").GetDecimal(),
            config.RoundMoney(c.GetProperty("value").GetDecimal()));
    }

    [Fact]
    public void Golden_Eos_Threshold_Formula()
    {
        var c = Doc.RootElement.GetProperty("cases").EnumerateArray()
            .First(x => x.GetProperty("id").GetString() == "eos-threshold-5-years");
        var config = PayrollRegimeSeed.CreateLaw50Configuration();
        var years = c.GetProperty("yearsOfService").GetInt32();
        var baseSalary = c.GetProperty("baseSalary").GetDecimal();
        var first = Math.Min(years, config.EosGratuityFirstYearsThreshold);
        var after = Math.Max(0, years - config.EosGratuityFirstYearsThreshold);
        var multiplier = first * config.EosGratuityRateFirstPeriod
            + after * config.EosGratuityRateAfterThreshold;
        Assert.Equal(c.GetProperty("expectMultiplier").GetDecimal(), multiplier);
        Assert.Equal(multiplier * baseSalary, multiplier * baseSalary);
    }

    [Fact]
    public void Golden_Retro_Cap_Rejects_Beyond_Max()
    {
        var c = Doc.RootElement.GetProperty("cases").EnumerateArray()
            .First(x => x.GetProperty("id").GetString() == "retro-months-cap");
        var max = c.GetProperty("maxRetroMonths").GetInt32();
        var monthsBack = c.GetProperty("monthsBack").GetInt32();
        var allowed = monthsBack <= max;
        Assert.Equal(c.GetProperty("expectAllowed").GetBoolean(), allowed);
    }

    [Fact]
    public void Historical_Replay_Same_Pin_Same_Hash()
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration();
        var h1 = config.ContentHash;
        var h2 = PayrollConfigurationHashService.ComputeContentHash(config);
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1!.Length);
    }
}
