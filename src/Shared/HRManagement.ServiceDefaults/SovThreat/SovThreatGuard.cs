using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.SovThreat;

public sealed class SovThreatMetrics
{
    public const string MeterName = "HRManagement.SovThreat";

    private readonly ISovThreatProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;

    public SovThreatMetrics(ISovThreatProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "sov_threat_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "SOV-005 audit required field count");
        meter.CreateObservableGauge(
            "sov_threat_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "SOV-005 core policy flags asserted");
        meter.CreateObservableGauge(
            "sov_threat_live_threat_model_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveThreatModelClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveThreatModelClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.Methodology?.StrideRequired == true ? 1 : 0) +
            (m.HighThreatCoverage?.ControlRequired == true ? 1 : 0) +
            (m.ChangeTrigger?.MaterialChangeUpdateRequired == true ? 1 : 0) +
            (m.ResidualRisk?.SignedRequired == true ? 1 : 0) +
            (m.MockSandbox?.LiveIndependentTmReviewForbidden == true ? 1 : 0);
    }
}

public sealed class ThreatModelClaimGuard(
    ISovThreatProvider provider,
    ILogger<ThreatModelClaimGuard> logger) : IThreatModelClaimGuard
{
    public string? EnsureLiveThreatModelAllowed()
    {
        if (!provider.Options.LiveThreatModelClaim)
            return null;
        var err = "LiveThreatModelClaim forbidden until SOV-005a live independent TM review";
        logger.LogError("SOV-005 threat-model claim: {Error}", err);
        return err;
    }

    public string? ValidateMethodology(bool stridePresent)
    {
        var err = SovThreatEvaluator.ValidateMethodology(stridePresent, provider.Matrix.Methodology);
        if (err is not null)
            logger.LogError("SOV-005 methodology gate: {Error}", err);
        return err;
    }

    public string? ValidateHighThreatCoverage(bool controlPresent, bool testPresent, bool ownerPresent)
    {
        var err = SovThreatEvaluator.ValidateHighThreatCoverage(
            controlPresent, testPresent, ownerPresent, provider.Matrix.HighThreatCoverage);
        if (err is not null)
            logger.LogError("SOV-005 high-coverage gate: {Error}", err);
        return err;
    }

    public string? ValidateChangeTrigger(bool materialChangeCovered)
    {
        var err = SovThreatEvaluator.ValidateChangeTrigger(materialChangeCovered, provider.Matrix.ChangeTrigger);
        if (err is not null)
            logger.LogError("SOV-005 change-trigger gate: {Error}", err);
        return err;
    }

    public string? ValidateMisuseLinkage(bool backlogLinked)
    {
        var err = SovThreatEvaluator.ValidateMisuseLinkage(backlogLinked, provider.Matrix.MisuseStories);
        if (err is not null)
            logger.LogError("SOV-005 misuse gate: {Error}", err);
        return err;
    }

    public string? ValidateResidualRisk(bool signed)
    {
        var err = SovThreatEvaluator.ValidateResidualRisk(signed, provider.Matrix.ResidualRisk);
        if (err is not null)
            logger.LogError("SOV-005 residual gate: {Error}", err);
        return err;
    }

    public string? ValidateCategoryCoverage(IEnumerable<string> missingCategories)
    {
        var err = SovThreatEvaluator.ValidateCategoryCoverage(missingCategories);
        if (err is not null)
            logger.LogError("SOV-005 category gate: {Error}", err);
        return err;
    }

    public string? ValidateTrustBoundaries(bool inventoryPresent)
    {
        var err = SovThreatEvaluator.ValidateTrustBoundaries(inventoryPresent, provider.Matrix.TrustBoundaries);
        if (err is not null)
            logger.LogError("SOV-005 trust-boundary gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = SovThreatEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("SOV-005 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(SovThreatSigning? meta)
    {
        var err = SovThreatEvaluator.ValidateSigning(meta, provider.Matrix.ThreatModelPolicyVersion);
        if (err is not null)
            logger.LogError("SOV-005 signing gate: {Error}", err);
        return err;
    }
}

public sealed class SovThreatStartupValidator(
    ISovThreatProvider provider,
    IThreatModelClaimGuard claimGuard,
    SovThreatMetrics metrics,
    IHostEnvironment environment,
    ILogger<SovThreatStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = SovThreatEvaluator.Evaluate(provider.Matrix, opts.LiveThreatModelClaim);

        var claimErr = claimGuard.EnsureLiveThreatModelAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("SOV-005 threat-model rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "SOV-005 SovThreat startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "SOV-005: {Count} threat-model rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "SOV-005 sov threat active enforce={Enforce} liveTm={Claim} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveThreatModelClaim,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
