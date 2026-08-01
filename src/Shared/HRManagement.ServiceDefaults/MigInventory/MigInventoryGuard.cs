using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.MigInventory;

public sealed class MigInventoryMetrics
{
    public const string MeterName = "HRManagement.MigInventory";

    private readonly IMigInventoryProvider _provider;
    private long _sourceCount;
    private long _sotCount;
    private long _liveExtractClaim;

    public MigInventoryMetrics(IMigInventoryProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "mig_inventory_source_count",
            () => _sourceCount,
            unit: "{sources}",
            description: "MIG-001 inventoried source count");
        meter.CreateObservableGauge(
            "mig_inventory_sot_count",
            () => _sotCount,
            unit: "{fields}",
            description: "MIG-001 target field SoT row count");
        meter.CreateObservableGauge(
            "mig_inventory_live_extract_claim",
            () => _liveExtractClaim,
            unit: "{bool}",
            description: "1 when LiveAgencyExtractClaim is true");
    }

    public void Recount()
    {
        _sourceCount = _provider.Matrix.Sources.Count;
        _sotCount = _provider.Matrix.TargetFieldSot.Count;
        _liveExtractClaim = _provider.Options.LiveAgencyExtractClaim ? 1 : 0;
    }
}

public sealed class LegacyExtractClaimGuard(
    IMigInventoryProvider provider,
    ILogger<LegacyExtractClaimGuard> logger) : ILegacyExtractClaimGuard
{
    public string? EnsureLiveAgencyExtractAllowed()
    {
        if (!provider.Options.LiveAgencyExtractClaim)
            return null;

        var agency = provider.Matrix.Blockers.FirstOrDefault(b =>
            string.Equals(b.Id, "agency-extract-access", StringComparison.OrdinalIgnoreCase));
        if (agency is null || string.Equals(agency.Status, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            var err = "LiveAgencyExtractClaim forbidden while agency-extract-access is Blocked";
            logger.LogError("MIG-001 extract claim: {Error}", err);
            return err;
        }

        return null;
    }

    public IReadOnlyList<string> ValidateSourceCoverage(MigInventoryMatrixDocument matrix)
    {
        var failures = MigInventoryEvaluator.EvaluateCoverage(matrix);
        foreach (var f in failures)
            logger.LogError("MIG-001 coverage gate: {Error}", f);
        return failures;
    }

    public string? ValidateFieldSot(MigFieldSot row, IReadOnlySet<string> sourceIds)
    {
        var err = MigInventoryEvaluator.ValidateFieldSot(row, sourceIds);
        if (err is not null)
            logger.LogError("MIG-001 SoT gate: {Error}", err);
        return err;
    }

    public string? ValidateInventoriedSource(MigSource source)
    {
        var err = MigInventoryEvaluator.ValidateInventoriedSource(source);
        if (err is not null)
            logger.LogError("MIG-001 source gate: {Error}", err);
        return err;
    }
}

public sealed class MigInventoryStartupValidator(
    IMigInventoryProvider provider,
    ILegacyExtractClaimGuard extractGuard,
    MigInventoryMetrics metrics,
    IHostEnvironment environment,
    ILogger<MigInventoryStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = MigInventoryEvaluator.Evaluate(provider.Matrix, opts.LiveAgencyExtractClaim);

        var claimErr = extractGuard.EnsureLiveAgencyExtractAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("MIG-001 inventory rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "MIG-001 MigInventory startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "MIG-001: {Count} inventory rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "MIG-001 mig inventory active enforce={Enforce} liveExtract={Claim} sources={Sources} sot={Sot} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveAgencyExtractClaim,
            provider.Matrix.Sources.Count,
            provider.Matrix.TargetFieldSot.Count,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
