using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.MigCutover;

public sealed class MigCutoverMetrics
{
    public const string MeterName = "HRManagement.MigCutover";

    private readonly IMigCutoverProvider _provider;
    private long _rehearsalCount;
    private long _criteriaCount;
    private long _liveClaim;

    public MigCutoverMetrics(IMigCutoverProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "mig_cutover_rehearsal_count",
            () => _rehearsalCount,
            unit: "{rehearsals}",
            description: "MIG-006 rehearsal count");
        meter.CreateObservableGauge(
            "mig_cutover_gonogo_criteria_count",
            () => _criteriaCount,
            unit: "{criteria}",
            description: "MIG-006 Go/No-Go criteria count");
        meter.CreateObservableGauge(
            "mig_cutover_live_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveAgencyCutoverClaim is true");
    }

    public void Recount()
    {
        _rehearsalCount = _provider.Matrix.Rehearsals.Count;
        _criteriaCount = _provider.Matrix.GoNoGo?.Criteria.Count ?? 0;
        _liveClaim = _provider.Options.LiveAgencyCutoverClaim ? 1 : 0;
    }
}

public sealed class AgencyCutoverClaimGuard(
    IMigCutoverProvider provider,
    ILogger<AgencyCutoverClaimGuard> logger) : IAgencyCutoverClaimGuard
{
    public string? EnsureLiveAgencyCutoverAllowed()
    {
        if (!provider.Options.LiveAgencyCutoverClaim)
            return null;
        var err = "LiveAgencyCutoverClaim forbidden until MIG-006a-d cutover workers";
        logger.LogError("MIG-006 agency cutover claim: {Error}", err);
        return err;
    }

    public string? ValidateRehearsal(MigCutoverRehearsal rehearsal)
    {
        var err = MigCutoverEvaluator.ValidateRehearsal(rehearsal);
        if (err is not null)
            logger.LogError("MIG-006 rehearsal gate: {Error}", err);
        return err;
    }

    public string? ValidateDelta(int lostCount)
    {
        var err = MigCutoverEvaluator.ValidateDelta(lostCount, provider.Matrix.DeltaCapture);
        if (err is not null)
            logger.LogError("MIG-006 delta gate: {Error}", err);
        return err;
    }

    public string? ValidateRollback(bool exercised, bool paperOnly)
    {
        var err = MigCutoverEvaluator.ValidateRollback(exercised, paperOnly, provider.Matrix.Rollback);
        if (err is not null)
            logger.LogError("MIG-006 rollback gate: {Error}", err);
        return err;
    }

    public string? ValidateGoNoGo(IReadOnlyDictionary<string, bool> criteriaMet)
    {
        var err = MigCutoverEvaluator.ValidateGoNoGo(criteriaMet, provider.Matrix.GoNoGo);
        if (err is not null)
            logger.LogError("MIG-006 go/nogo gate: {Error}", err);
        return err;
    }

    public string? ValidateLegacyAccess(bool readOnly, bool secured)
    {
        var err = MigCutoverEvaluator.ValidateLegacyAccess(readOnly, secured, provider.Matrix.LegacyAccess);
        if (err is not null)
            logger.LogError("MIG-006 legacy gate: {Error}", err);
        return err;
    }

    public string? ValidateLiveDnsClaim(bool liveDns)
    {
        var err = MigCutoverEvaluator.ValidateLiveDnsClaim(liveDns, provider.Matrix.DnsCutover);
        if (err is not null)
            logger.LogError("MIG-006 DNS gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(MigCutoverSigning? meta)
    {
        var err = MigCutoverEvaluator.ValidateSigning(meta, provider.Matrix.CutoverPolicyVersion);
        if (err is not null)
            logger.LogError("MIG-006 signing gate: {Error}", err);
        return err;
    }
}

public sealed class MigCutoverStartupValidator(
    IMigCutoverProvider provider,
    IAgencyCutoverClaimGuard claimGuard,
    MigCutoverMetrics metrics,
    IHostEnvironment environment,
    ILogger<MigCutoverStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = MigCutoverEvaluator.Evaluate(provider.Matrix, opts.LiveAgencyCutoverClaim);

        var claimErr = claimGuard.EnsureLiveAgencyCutoverAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("MIG-006 cutover rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "MIG-006 MigCutover startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "MIG-006: {Count} cutover rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "MIG-006 mig cutover active enforce={Enforce} liveCutover={Claim} rehearsals={Rehearsals} criteria={Criteria} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveAgencyCutoverClaim,
            provider.Matrix.Rehearsals.Count,
            provider.Matrix.GoNoGo?.Criteria.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
