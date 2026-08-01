using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.SovBaseline;

public sealed class SovBaselineMetrics
{
    public const string MeterName = "HRManagement.SovBaseline";

    private readonly ISovBaselineProvider _provider;
    private long _auditFieldCount;
    private long _crosswalkCount;
    private long _liveClaim;
    private long _policyFlags;

    public SovBaselineMetrics(ISovBaselineProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "sov_baseline_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "SOV-001 audit required field count");
        meter.CreateObservableGauge(
            "sov_baseline_crosswalk_count",
            () => _crosswalkCount,
            unit: "{frameworks}",
            description: "SOV-001 crosswalk framework count");
        meter.CreateObservableGauge(
            "sov_baseline_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "SOV-001 core policy flags asserted");
        meter.CreateObservableGauge(
            "sov_baseline_live_ato_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveAtoClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _crosswalkCount = _provider.Matrix.Frameworks?.Crosswalk.Count ?? 0;
        _liveClaim = _provider.Options.LiveAtoClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.ControlCoverage?.FullCoverageRequired == true ? 1 : 0) +
            (m.OwnershipEvidence?.OwnerRequired == true ? 1 : 0) +
            (m.Poam?.DatedRemediationRequired == true ? 1 : 0) +
            (m.IndependentAssessment?.LiveAtoForbidden == true ? 1 : 0) +
            (m.MockSandbox?.LiveAtoForbidden == true ? 1 : 0);
    }
}

public sealed class AtoClaimGuard(
    ISovBaselineProvider provider,
    ILogger<AtoClaimGuard> logger) : IAtoClaimGuard
{
    public string? EnsureLiveAtoAllowed()
    {
        if (!provider.Options.LiveAtoClaim)
            return null;
        var err = "LiveAtoClaim forbidden until SOV-001a live independent ATO";
        logger.LogError("SOV-001 ATO claim: {Error}", err);
        return err;
    }

    public string? ValidateControlOwnership(bool hasOwner, bool hasEvidence)
    {
        var err = SovBaselineEvaluator.ValidateControlOwnership(
            hasOwner, hasEvidence, provider.Matrix.OwnershipEvidence);
        if (err is not null)
            logger.LogError("SOV-001 ownership gate: {Error}", err);
        return err;
    }

    public string? ValidateCoverage(bool statusKnown, bool siteReasonPresentIfNa)
    {
        var err = SovBaselineEvaluator.ValidateCoverage(
            statusKnown, siteReasonPresentIfNa, provider.Matrix.ControlCoverage);
        if (err is not null)
            logger.LogError("SOV-001 coverage gate: {Error}", err);
        return err;
    }

    public string? ValidatePoam(bool datedRemediationPresent)
    {
        var err = SovBaselineEvaluator.ValidatePoam(datedRemediationPresent, provider.Matrix.Poam);
        if (err is not null)
            logger.LogError("SOV-001 POA&M gate: {Error}", err);
        return err;
    }

    public string? ValidateException(bool signed)
    {
        var err = SovBaselineEvaluator.ValidateException(signed, provider.Matrix.Exceptions);
        if (err is not null)
            logger.LogError("SOV-001 exception gate: {Error}", err);
        return err;
    }

    public string? ValidateAuthorizationBoundary(bool boundaryPresent)
    {
        var err = SovBaselineEvaluator.ValidateAuthorizationBoundary(
            boundaryPresent, provider.Matrix.AuthorizationBoundary);
        if (err is not null)
            logger.LogError("SOV-001 boundary gate: {Error}", err);
        return err;
    }

    public string? ValidateReassessment(bool triggeredAfterMaterialChange)
    {
        var err = SovBaselineEvaluator.ValidateReassessment(
            triggeredAfterMaterialChange, provider.Matrix.Reassessment);
        if (err is not null)
            logger.LogError("SOV-001 reassessment gate: {Error}", err);
        return err;
    }

    public string? ValidateIndependentAssessment(bool samplePresent)
    {
        var err = SovBaselineEvaluator.ValidateIndependentAssessment(samplePresent);
        if (err is not null)
            logger.LogError("SOV-001 assessment gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = SovBaselineEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("SOV-001 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(SovBaselineSigning? meta)
    {
        var err = SovBaselineEvaluator.ValidateSigning(meta, provider.Matrix.ControlBaselinePolicyVersion);
        if (err is not null)
            logger.LogError("SOV-001 signing gate: {Error}", err);
        return err;
    }
}

public sealed class SovBaselineStartupValidator(
    ISovBaselineProvider provider,
    IAtoClaimGuard claimGuard,
    SovBaselineMetrics metrics,
    IHostEnvironment environment,
    ILogger<SovBaselineStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = SovBaselineEvaluator.Evaluate(provider.Matrix, opts.LiveAtoClaim);

        var claimErr = claimGuard.EnsureLiveAtoAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("SOV-001 control-baseline rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "SOV-001 SovBaseline startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "SOV-001: {Count} control-baseline rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "SOV-001 sov baseline active enforce={Enforce} liveAto={Claim} crosswalk={Count} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveAtoClaim,
            provider.Matrix.Frameworks?.Crosswalk.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
