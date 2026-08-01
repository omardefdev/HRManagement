using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.MigCanonical;

public sealed class MigCanonicalMetrics
{
    public const string MeterName = "HRManagement.MigCanonical";

    private readonly IMigCanonicalProvider _provider;
    private long _mapCount;
    private long _entityCount;
    private long _liveClaim;

    public MigCanonicalMetrics(IMigCanonicalProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "mig_canonical_code_map_count",
            () => _mapCount,
            unit: "{maps}",
            description: "MIG-002 code map count");
        meter.CreateObservableGauge(
            "mig_canonical_entity_count",
            () => _entityCount,
            unit: "{entities}",
            description: "MIG-002 canonical entity count");
        meter.CreateObservableGauge(
            "mig_canonical_live_engine_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveCodeMapEngineClaim is true");
    }

    public void Recount()
    {
        _mapCount = _provider.Matrix.CodeMaps.Count;
        _entityCount = _provider.Matrix.CanonicalEntities.Count;
        _liveClaim = _provider.Options.LiveCodeMapEngineClaim ? 1 : 0;
    }
}

public sealed class CodeMapClaimGuard(
    IMigCanonicalProvider provider,
    ILogger<CodeMapClaimGuard> logger) : ICodeMapClaimGuard
{
    public string? EnsureLiveCodeMapEngineAllowed()
    {
        if (!provider.Options.LiveCodeMapEngineClaim)
            return null;
        var err = "LiveCodeMapEngineClaim forbidden until MIG-003b / LiveEtlWorkersClaim";
        logger.LogError("MIG-002 code-map claim: {Error}", err);
        return err;
    }

    public string? ValidateDisposition(MigCodeMap map)
    {
        var err = MigCanonicalEvaluator.ValidateDisposition(
            map,
            provider.Matrix.AllowedDispositions,
            provider.Matrix.UnmappedPolicy?.ForbiddenCatchAllValues);
        if (err is not null)
            logger.LogError("MIG-002 disposition gate: {Error}", err);
        return err;
    }

    public string? ValidateLegacyIdUse(string proposedKey)
    {
        var err = MigCanonicalEvaluator.ValidateLegacyIdUse(proposedKey, provider.Matrix.LegacyIdPolicy);
        if (err is not null)
            logger.LogError("MIG-002 legacy-id gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(MigCodeMapSigning? meta)
    {
        var err = MigCanonicalEvaluator.ValidateSigning(meta, provider.Matrix.MappingVersion);
        if (err is not null)
            logger.LogError("MIG-002 signing gate: {Error}", err);
        return err;
    }
}

public sealed class MigCanonicalStartupValidator(
    IMigCanonicalProvider provider,
    ICodeMapClaimGuard claimGuard,
    MigCanonicalMetrics metrics,
    IHostEnvironment environment,
    ILogger<MigCanonicalStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = MigCanonicalEvaluator.Evaluate(provider.Matrix, opts.LiveCodeMapEngineClaim);

        var claimErr = claimGuard.EnsureLiveCodeMapEngineAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("MIG-002 code-map rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "MIG-002 MigCanonical startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "MIG-002: {Count} code-map rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "MIG-002 mig canonical active enforce={Enforce} liveEngine={Claim} maps={Maps} entities={Entities} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveCodeMapEngineClaim,
            provider.Matrix.CodeMaps.Count,
            provider.Matrix.CanonicalEntities.Count,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
