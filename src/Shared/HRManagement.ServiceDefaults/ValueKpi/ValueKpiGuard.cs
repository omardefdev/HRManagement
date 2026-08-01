using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ValueKpi;

public sealed class ValueKpiMetrics
{
    public const string MeterName = "HRManagement.ProgramValue";

    private readonly IValueKpiProvider _provider;
    private long _kpiCount;
    private long _frozenBaselines;
    private long _liveDashboardClaim;

    public ValueKpiMetrics(IValueKpiProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "program_value_kpi_count",
            () => _kpiCount,
            unit: "{kpis}",
            description: "STR-007 KPI catalog size");
        meter.CreateObservableGauge(
            "program_value_frozen_baseline_count",
            () => _frozenBaselines,
            unit: "{baselines}",
            description: "Baselines with prePilotFreeze");
        meter.CreateObservableGauge(
            "program_value_live_dashboard_claim",
            () => _liveDashboardClaim,
            unit: "{bool}",
            description: "1 when LiveDashboardClaim is true");
    }

    public void Recount()
    {
        _kpiCount = _provider.Matrix.Kpis.Count;
        _frozenBaselines = _provider.Matrix.Baselines.Count(b => b.PrePilotFreeze);
        _liveDashboardClaim = _provider.Options.LiveDashboardClaim ? 1 : 0;
    }
}

public sealed class ValueBenefitGuard(
    IValueKpiProvider provider,
    ILogger<ValueBenefitGuard> logger) : IValueBenefitGuard
{
    public string? EnsureLiveDashboardAllowed()
    {
        if (!provider.Options.LiveDashboardClaim)
            return null;

        if (!ValueKpiEvaluator.PrivacyIsValid(provider.Matrix))
        {
            var err = "LiveDashboardClaim requires valid tenant privacy rules";
            logger.LogError("STR-007 value claim: {Error}", err);
            return err;
        }

        var forbidden = "LiveDashboardClaim forbidden until PROD-006c / live-bi-dashboard";
        logger.LogError("STR-007 value claim: {Error}", forbidden);
        return forbidden;
    }

    public string? ValidateBenefitMeasure(string measureId)
    {
        var err = ValueKpiEvaluator.ValidateBenefitMeasure(
            measureId, provider.Matrix.ForbiddenBenefitMeasures);
        if (err is not null)
            logger.LogError("STR-007 benefit gate: {Error}", err);
        return err;
    }

    public string? ValidateKpiPair(ValueKpiDefinition kpi)
    {
        var err = ValueKpiEvaluator.ValidateKpiPair(kpi);
        if (err is not null)
            logger.LogError("STR-007 KPI gate: {Error}", err);
        return err;
    }

    public string? ValidateBaseline(string kpiId)
    {
        var err = ValueKpiEvaluator.ValidateBaseline(provider.Matrix, kpiId);
        if (err is not null)
            logger.LogError("STR-007 baseline gate: {Error}", err);
        return err;
    }
}

public sealed class ValueKpiStartupValidator(
    IValueKpiProvider provider,
    IValueBenefitGuard benefitGuard,
    ValueKpiMetrics metrics,
    IHostEnvironment environment,
    ILogger<ValueKpiStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ValueKpiEvaluator.Evaluate(provider.Matrix, opts.LiveDashboardClaim);

        var claimErr = benefitGuard.EnsureLiveDashboardAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("STR-007 value KPI rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "STR-007 ProgramValue startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "STR-007: {Count} value KPI rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "STR-007 program value active enforce={Enforce} liveDashboard={Claim} kpis={Kpis} frozenBaselines={Baselines} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveDashboardClaim,
            provider.Matrix.Kpis.Count,
            provider.Matrix.Baselines.Count(b => b.PrePilotFreeze),
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
