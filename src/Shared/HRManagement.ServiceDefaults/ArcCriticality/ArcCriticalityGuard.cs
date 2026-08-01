using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ArcCriticality;

public sealed class ArcCriticalityMetrics
{
    public const string MeterName = "HRManagement.ArcCriticality";

    private readonly IArcCriticalityProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;

    public ArcCriticalityMetrics(IArcCriticalityProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "arc_criticality_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "ARC-001 audit required field count");
        meter.CreateObservableGauge(
            "arc_criticality_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "ARC-001 core policy flags asserted");
        meter.CreateObservableGauge(
            "arc_criticality_live_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveCriticalityClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveCriticalityClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.TierClassification?.AllServicesClassifiedRequired == true ? 1 : 0) +
            (m.SpofPolicy?.NoUnacceptableSpofInTier01Required == true ? 1 : 0) +
            (m.NumericTargetsTelemetry?.EveryNumericTargetHasTelemetryRequired == true ? 1 : 0) +
            (m.RecoveryOrder?.OrderedStepsRequired == true ? 1 : 0) +
            (m.MockSandbox?.LiveArchitectureFailureWalkthroughForbidden == true ? 1 : 0);
    }
}

public sealed class CriticalityClaimGuard(
    IArcCriticalityProvider provider,
    ILogger<CriticalityClaimGuard> logger) : ICriticalityClaimGuard
{
    public string? EnsureLiveCriticalityAllowed()
    {
        if (!provider.Options.LiveCriticalityClaim)
            return null;
        var err = "LiveCriticalityClaim forbidden until ARC-001a live architecture failure walkthrough";
        logger.LogError("ARC-001 criticality claim: {Error}", err);
        return err;
    }

    public string? ValidateClassification(bool allServicesClassified, bool allDependenciesClassified)
    {
        var err = ArcCriticalityEvaluator.ValidateClassification(
            allServicesClassified, allDependenciesClassified, provider.Matrix.TierClassification);
        if (err is not null)
            logger.LogError("ARC-001 classification gate: {Error}", err);
        return err;
    }

    public string? ValidateJourneyMap(bool criticalJourneysMapped)
    {
        var err = ArcCriticalityEvaluator.ValidateJourneyMap(criticalJourneysMapped, provider.Matrix.JourneyDependencyMap);
        if (err is not null)
            logger.LogError("ARC-001 journey gate: {Error}", err);
        return err;
    }

    public string? ValidateSharedPlatform(IEnumerable<string> missingSurfaces)
    {
        var err = ArcCriticalityEvaluator.ValidateSharedPlatform(missingSurfaces);
        if (err is not null)
            logger.LogError("ARC-001 platform gate: {Error}", err);
        return err;
    }

    public string? ValidateTelemetryBinding(bool targetWithoutTelemetry)
    {
        var err = ArcCriticalityEvaluator.ValidateTelemetryBinding(
            targetWithoutTelemetry, provider.Matrix.NumericTargetsTelemetry);
        if (err is not null)
            logger.LogError("ARC-001 telemetry gate: {Error}", err);
        return err;
    }

    public string? ValidateSpof(bool unacceptableSpofInTier01)
    {
        var err = ArcCriticalityEvaluator.ValidateSpof(unacceptableSpofInTier01, provider.Matrix.SpofPolicy);
        if (err is not null)
            logger.LogError("ARC-001 spof gate: {Error}", err);
        return err;
    }

    public string? ValidateRecoveryOrder(bool orderedStepsPresent, bool drillEvidencePresent)
    {
        var err = ArcCriticalityEvaluator.ValidateRecoveryOrder(
            orderedStepsPresent, drillEvidencePresent, provider.Matrix.RecoveryOrder);
        if (err is not null)
            logger.LogError("ARC-001 recovery gate: {Error}", err);
        return err;
    }

    public string? ValidateFailureDomains(bool domainsDocumented)
    {
        var err = ArcCriticalityEvaluator.ValidateFailureDomains(domainsDocumented, provider.Matrix.FailureDomains);
        if (err is not null)
            logger.LogError("ARC-001 failure-domain gate: {Error}", err);
        return err;
    }

    public string? ValidateGracefulDegradation(bool tier01Documented)
    {
        var err = ArcCriticalityEvaluator.ValidateGracefulDegradation(
            tier01Documented, provider.Matrix.GracefulDegradation);
        if (err is not null)
            logger.LogError("ARC-001 degradation gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ArcCriticalityEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("ARC-001 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ArcCriticalitySigning? meta)
    {
        var err = ArcCriticalityEvaluator.ValidateSigning(meta, provider.Matrix.CriticalityPolicyVersion);
        if (err is not null)
            logger.LogError("ARC-001 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ArcCriticalityStartupValidator(
    IArcCriticalityProvider provider,
    ICriticalityClaimGuard claimGuard,
    ArcCriticalityMetrics metrics,
    IHostEnvironment environment,
    ILogger<ArcCriticalityStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ArcCriticalityEvaluator.Evaluate(provider.Matrix, opts.LiveCriticalityClaim);

        var claimErr = claimGuard.EnsureLiveCriticalityAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("ARC-001 criticality rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "ARC-001 ArcCriticality startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "ARC-001: {Count} criticality rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "ARC-001 arc criticality active enforce={Enforce} liveCriticality={Claim} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveCriticalityClaim,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
