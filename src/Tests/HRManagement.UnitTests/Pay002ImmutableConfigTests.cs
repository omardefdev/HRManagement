using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace HRManagement.UnitTests;

public class Pay002ImmutableConfigTests
{
    private static PayrollConfiguration PublishedLaw50(DateTime? effectiveFrom = null)
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        config.EffectiveFrom = effectiveFrom ?? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        return config;
    }

    [Fact]
    public void Calculate_WithoutPublishedConfig_Fails_Resolver()
    {
        var configs = Array.Empty<PayrollConfiguration>();
        var resolved = PayrollConfigurationVersioning.ResolvePublishedForPeriod(
            configs, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Null(resolved);
    }

    [Fact]
    public void Calculate_PinsConfigurationIdAndHash_ViaResolve()
    {
        var config = PublishedLaw50();
        var resolved = PayrollConfigurationVersioning.ResolvePublishedForPeriod(
            new[] { config }, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(resolved);
        Assert.Equal(config.Id, resolved!.Id);
        Assert.False(string.IsNullOrEmpty(resolved.ContentHash));
        Assert.Equal(64, resolved.ContentHash!.Length);
    }

    [Fact]
    public void EditPublished_CreatesNewDraftVersion()
    {
        var published = PublishedLaw50();
        var draft = PayrollConfigurationVersioning.CloneAsDraft(published, nextVersionNumber: 2, c =>
        {
            c.TaxExemptionAmount = 1000m;
        });

        Assert.Equal(PayrollConfigPublicationStatus.Draft, draft.PublicationStatus);
        Assert.Equal(2, draft.VersionNumber);
        Assert.Equal(published.Id, draft.SupersedesConfigurationId);
        Assert.False(draft.IsActive);
        Assert.Null(draft.ContentHash);
        Assert.Equal(1000m, draft.TaxExemptionAmount);
        Assert.Equal(published.TaxBrackets.Count, draft.TaxBrackets.Count);
    }

    [Fact]
    public void MutatePublished_Rejected()
    {
        var published = PublishedLaw50();
        var err = PayrollConfigurationVersioning.GuardImmutableMutation(published);
        Assert.NotNull(err);
        Assert.Contains("منشورة", err);
    }

    [Fact]
    public void Publish_ActivatesSingleVersion()
    {
        var v1 = PublishedLaw50(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        v1.Id = Guid.NewGuid();
        var v2 = PayrollConfigurationVersioning.CloneAsDraft(v1, 2, c => c.TaxExemptionAmount = 500);
        v2.Id = Guid.NewGuid();
        PayrollConfigurationVersioning.TryApprove(v2, Guid.NewGuid());

        var list = new List<PayrollConfiguration> { v1, v2 };
        var (ok, error) = PayrollConfigurationVersioning.TryPublish(
            v2, list, Guid.NewGuid(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(PayrollConfigPublicationStatus.Published, v2.PublicationStatus);
        Assert.True(v2.IsActive);
        Assert.Equal(PayrollConfigPublicationStatus.Superseded, v1.PublicationStatus);
        Assert.False(v1.IsActive);
    }

    [Fact]
    public void Correction_InheritsSourceConfigPin()
    {
        var config = PublishedLaw50();
        var source = new PayrollRun
        {
            Id = Guid.NewGuid(),
            Status = PayrollRunStatus.Posted,
            PayrollConfigurationId = config.Id,
            ConfigContentHash = config.ContentHash,
            PeriodStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            RunDate = DateTime.UtcNow
        };

        Assert.True(source.PayrollConfigurationId.HasValue);
        Assert.Equal(config.ContentHash, source.ConfigContentHash);

        var correction = new PayrollRun
        {
            SourceRunId = source.Id,
            PayrollConfigurationId = source.PayrollConfigurationId,
            ConfigContentHash = source.ConfigContentHash,
            RunType = PayrollRunType.Correction
        };
        Assert.Equal(source.PayrollConfigurationId, correction.PayrollConfigurationId);
        Assert.Equal(source.ConfigContentHash, correction.ConfigContentHash);
    }

    [Fact]
    public void RoundingUsesPinnedConfig()
    {
        var config = PublishedLaw50();
        config.DecimalPlaces = 3;
        config.RoundingMode = PayrollRoundingMode.AwayFromZero;
        Assert.Equal(1.235m, config.RoundMoney(1.2345m));
    }

    [Fact]
    public void Boundary_EffectiveFrom_SelectsCorrectVersion()
    {
        var old = PublishedLaw50(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        old.Id = Guid.NewGuid();
        old.VersionNumber = 1;
        old.TaxExemptionAmount = 0;
        old.ContentHash = PayrollConfigurationHashService.ComputeContentHash(old);

        var newer = PublishedLaw50(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        newer.Id = Guid.NewGuid();
        newer.VersionNumber = 2;
        newer.TaxExemptionAmount = 999;
        newer.ContentHash = PayrollConfigurationHashService.ComputeContentHash(newer);

        var for2025 = PayrollConfigurationVersioning.ResolvePublishedForPeriod(
            new[] { old, newer }, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(old.Id, for2025!.Id);

        var for2026 = PayrollConfigurationVersioning.ResolvePublishedForPeriod(
            new[] { old, newer }, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(newer.Id, for2026!.Id);

        var beforeAll = PayrollConfigurationVersioning.ResolvePublishedForPeriod(
            new[] { newer }, new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Null(beforeAll);
    }

    [Fact]
    public void Replay_SameInputsAndPinnedConfig_BitForBit()
    {
        var config = PublishedLaw50();
        var engine = PayrollEngine.CreateDefaultPipeline();
        var periodStart = new DateOnly(2026, 1, 1);
        var periodEnd = new DateOnly(2026, 1, 31);

        PayrollCalculationContext Ctx() => new()
        {
            PayrollRunId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            WorkingDaysInPeriod = 22,
            EmployeeWorkingDays = 22,
            MonthlyBaseSalary = 100_000m,
            Allowances = Array.Empty<AllowanceRecord>(),
            Deductions = Array.Empty<DeductionRecord>(),
            StatutoryDeductionTypes = Array.Empty<DeductionType>(),
            Adjustments = Array.Empty<PayrollAdjustment>(),
            AbsenceDeductions = Array.Empty<AbsenceDeduction>(),
            Configuration = config,
            ExtraHolidays = new HashSet<DateOnly>()
        };

        var a = engine.Calculate(Ctx());
        var b = engine.Calculate(Ctx());
        Assert.Equal(a.NetPay, b.NetPay);
        Assert.Equal(a.GrossPay, b.GrossPay);
        Assert.Equal(a.TotalDeductions, b.TotalDeductions);

        // Publishing a different active config must not change replay on the pinned instance.
        var mutatedActive = PayrollConfigurationVersioning.CloneAsDraft(config, 2, c => c.TaxExemptionAmount = 50_000m);
        mutatedActive.PublicationStatus = PayrollConfigPublicationStatus.Published;
        mutatedActive.ContentHash = PayrollConfigurationHashService.ComputeContentHash(mutatedActive);

        var replay = engine.Calculate(Ctx());
        Assert.Equal(a.NetPay, replay.NetPay);
        Assert.NotEqual(
            PayrollConfigurationHashService.ComputeContentHash(mutatedActive),
            config.ContentHash);
    }
}
