using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.MigMatch;

public sealed class MigMatchMetrics
{
    public const string MeterName = "HRManagement.MigMatch";

    private readonly IMigMatchProvider _provider;
    private long _strategyCount;
    private long _fraudCount;
    private long _liveClaim;

    public MigMatchMetrics(IMigMatchProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "mig_match_strategy_count",
            () => _strategyCount,
            unit: "{strategies}",
            description: "MIG-004 match strategy count");
        meter.CreateObservableGauge(
            "mig_match_fraud_indicator_count",
            () => _fraudCount,
            unit: "{indicators}",
            description: "MIG-004 fraud indicator count");
        meter.CreateObservableGauge(
            "mig_match_live_identity_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveIdentityMatchClaim is true");
    }

    public void Recount()
    {
        _strategyCount = _provider.Matrix.MatchStrategies.Count;
        _fraudCount = _provider.Matrix.FraudIndicators.Count;
        _liveClaim = _provider.Options.LiveIdentityMatchClaim ? 1 : 0;
    }
}

public sealed class IdentityMatchClaimGuard(
    IMigMatchProvider provider,
    ILogger<IdentityMatchClaimGuard> logger) : IIdentityMatchClaimGuard
{
    public string? EnsureLiveIdentityMatchAllowed()
    {
        if (!provider.Options.LiveIdentityMatchClaim)
            return null;
        var err = "LiveIdentityMatchClaim forbidden until MIG-004a-d matcher workers";
        logger.LogError("MIG-004 identity match claim: {Error}", err);
        return err;
    }

    public string? ValidateProposedOutcome(string? outcome, string? risk, string? evidenceRef)
    {
        var err = MigMatchEvaluator.ValidateProposedOutcome(
            outcome, risk, evidenceRef,
            provider.Matrix.Thresholds,
            provider.Matrix.FalsePositiveControls);
        if (err is not null)
            logger.LogError("MIG-004 outcome gate: {Error}", err);
        return err;
    }

    public string? ValidateMergeAudit(IReadOnlyDictionary<string, string?> fields, bool reversible)
    {
        var err = MigMatchEvaluator.ValidateMergeAuditFields(fields, reversible, provider.Matrix.MergeAudit);
        if (err is not null)
            logger.LogError("MIG-004 merge audit gate: {Error}", err);
        return err;
    }

    public string? ValidateReviewPrivacy(bool crossTenant)
    {
        var err = MigMatchEvaluator.ValidateReviewPrivacy(crossTenant, provider.Matrix.ReviewPrivacy);
        if (err is not null)
            logger.LogError("MIG-004 review privacy gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(MigMatchSigning? meta)
    {
        var err = MigMatchEvaluator.ValidateSigning(meta, provider.Matrix.MatchPolicyVersion);
        if (err is not null)
            logger.LogError("MIG-004 signing gate: {Error}", err);
        return err;
    }

    public string? ValidateLiveMetricsClaim(bool liveAgencyEval)
    {
        var err = MigMatchEvaluator.ValidateLiveMetricsClaim(liveAgencyEval, provider.Matrix);
        if (err is not null)
            logger.LogError("MIG-004 live metrics gate: {Error}", err);
        return err;
    }
}

public sealed class MigMatchStartupValidator(
    IMigMatchProvider provider,
    IIdentityMatchClaimGuard claimGuard,
    MigMatchMetrics metrics,
    IHostEnvironment environment,
    ILogger<MigMatchStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = MigMatchEvaluator.Evaluate(provider.Matrix, opts.LiveIdentityMatchClaim);

        var claimErr = claimGuard.EnsureLiveIdentityMatchAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("MIG-004 match rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "MIG-004 MigMatch startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "MIG-004: {Count} match rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "MIG-004 mig match active enforce={Enforce} liveMatch={Claim} strategies={Strategies} fraud={Fraud} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveIdentityMatchClaim,
            provider.Matrix.MatchStrategies.Count,
            provider.Matrix.FraudIndicators.Count,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
