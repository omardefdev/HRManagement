using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.MigFactory;

public sealed class MigFactoryMetrics
{
    public const string MeterName = "HRManagement.MigFactory";

    private readonly IMigFactoryProvider _provider;
    private long _pipelineCount;
    private long _zoneCount;
    private long _stageCount;
    private long _liveClaim;

    public MigFactoryMetrics(IMigFactoryProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "mig_factory_pipeline_count",
            () => _pipelineCount,
            unit: "{pipelines}",
            description: "MIG-003 pipeline count");
        meter.CreateObservableGauge(
            "mig_factory_zone_count",
            () => _zoneCount,
            unit: "{zones}",
            description: "MIG-003 staging zone count");
        meter.CreateObservableGauge(
            "mig_factory_stage_count",
            () => _stageCount,
            unit: "{stages}",
            description: "MIG-003 stage count");
        meter.CreateObservableGauge(
            "mig_factory_live_etl_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveEtlWorkersClaim is true");
    }

    public void Recount()
    {
        _pipelineCount = _provider.Matrix.Pipelines.Count;
        _zoneCount = _provider.Matrix.StagingZones.Count;
        _stageCount = _provider.Matrix.Stages.Count;
        _liveClaim = _provider.Options.LiveEtlWorkersClaim ? 1 : 0;
    }
}

public sealed class EtlWorkersClaimGuard(
    IMigFactoryProvider provider,
    ILogger<EtlWorkersClaimGuard> logger) : IEtlWorkersClaimGuard
{
    public string? EnsureLiveEtlWorkersAllowed()
    {
        if (!provider.Options.LiveEtlWorkersClaim)
            return null;
        var err = "LiveEtlWorkersClaim forbidden until MIG-003a-d ETL workers";
        logger.LogError("MIG-003 ETL claim: {Error}", err);
        return err;
    }

    public string? ValidateIdempotency(MigIdempotencyPolicy? policy)
    {
        var err = MigFactoryEvaluator.ValidateIdempotency(policy ?? provider.Matrix.Idempotency);
        if (err is not null)
            logger.LogError("MIG-003 idempotency gate: {Error}", err);
        return err;
    }

    public string? ValidateStagingZone(MigStagingZone zone)
    {
        var err = MigFactoryEvaluator.ValidateStagingZone(zone);
        if (err is not null)
            logger.LogError("MIG-003 staging zone gate: {Error}", err);
        return err;
    }

    public string? ValidateLineageFields(IReadOnlyList<string>? fields)
    {
        var err = MigFactoryEvaluator.ValidateLineageFields(
            fields ?? provider.Matrix.Lineage?.PerRecordFields);
        if (err is not null)
            logger.LogError("MIG-003 lineage gate: {Error}", err);
        return err;
    }

    public string? ValidatePiiLogSample(IEnumerable<string> sampleKeys)
    {
        var err = MigFactoryEvaluator.ValidatePiiLogSample(sampleKeys, provider.Matrix.PiiLogging);
        if (err is not null)
            logger.LogError("MIG-003 PII log gate: {Error}", err);
        return err;
    }

    public string? ValidateThroughputClaim(bool liveProof)
    {
        var err = MigFactoryEvaluator.ValidateThroughputClaim(liveProof, provider.Matrix);
        if (err is not null)
            logger.LogError("MIG-003 throughput gate: {Error}", err);
        return err;
    }
}

public sealed class MigFactoryStartupValidator(
    IMigFactoryProvider provider,
    IEtlWorkersClaimGuard claimGuard,
    MigFactoryMetrics metrics,
    IHostEnvironment environment,
    ILogger<MigFactoryStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = MigFactoryEvaluator.Evaluate(provider.Matrix, opts.LiveEtlWorkersClaim);

        var claimErr = claimGuard.EnsureLiveEtlWorkersAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("MIG-003 factory rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "MIG-003 MigFactory startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "MIG-003: {Count} factory rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "MIG-003 mig factory active enforce={Enforce} liveEtl={Claim} pipelines={Pipelines} zones={Zones} stages={Stages} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveEtlWorkersClaim,
            provider.Matrix.Pipelines.Count,
            provider.Matrix.StagingZones.Count,
            provider.Matrix.Stages.Count,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
