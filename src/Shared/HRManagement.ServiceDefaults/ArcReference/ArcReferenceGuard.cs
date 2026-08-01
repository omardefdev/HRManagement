using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ArcReference;

public sealed class ArcReferenceMetrics
{
    public const string MeterName = "HRManagement.ArcReference";

    private readonly IArcReferenceProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;

    public ArcReferenceMetrics(IArcReferenceProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "arc_reference_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "ARC-002 audit required field count");
        meter.CreateObservableGauge(
            "arc_reference_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "ARC-002 core policy flags asserted");
        meter.CreateObservableGauge(
            "arc_reference_live_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveReferenceClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveReferenceClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.BoundedContexts?.NoUnjustifiedDistributedComplexityRequired == true ? 1 : 0) +
            (m.MultiSiteProfiles?.SameArtifactPrimaryAndDrRequired == true ? 1 : 0) +
            (m.SupportedTopologies?.UnsupportedTopologyRejectedRequired == true ? 1 : 0) +
            (m.CouplingGuardrails?.NoDirectDbCrossBoundaryRequired == true ? 1 : 0) +
            (m.FitnessEnvelope?.LabFitnessHarnessRequired == true ? 1 : 0) +
            (m.MockSandbox?.LiveTwoSiteDeploymentForbidden == true ? 1 : 0);
    }
}

public sealed class ReferenceClaimGuard(
    IArcReferenceProvider provider,
    ILogger<ReferenceClaimGuard> logger) : IReferenceClaimGuard
{
    public string? EnsureLiveReferenceAllowed()
    {
        if (!provider.Options.LiveReferenceClaim)
            return null;
        var err = "LiveReferenceClaim forbidden until ARC-002a live two-site deployment";
        logger.LogError("ARC-002 reference claim: {Error}", err);
        return err;
    }

    public string? ValidateBoundedContexts(bool everyContextOwned, bool unjustifiedDistributionPresent)
    {
        var err = ArcReferenceEvaluator.ValidateBoundedContexts(
            everyContextOwned, unjustifiedDistributionPresent, provider.Matrix.BoundedContexts);
        if (err is not null)
            logger.LogError("ARC-002 contexts gate: {Error}", err);
        return err;
    }

    public string? ValidateMultiSite(bool sameArtifactPrimaryAndDr, bool primaryAndDrProfilesPresent)
    {
        var err = ArcReferenceEvaluator.ValidateMultiSite(
            sameArtifactPrimaryAndDr, primaryAndDrProfilesPresent, provider.Matrix.MultiSiteProfiles);
        if (err is not null)
            logger.LogError("ARC-002 multi-site gate: {Error}", err);
        return err;
    }

    public string? ValidateTopology(bool unsupportedTopologyPresent, bool envelopeValidated)
    {
        var err = ArcReferenceEvaluator.ValidateTopology(
            unsupportedTopologyPresent, envelopeValidated, provider.Matrix.SupportedTopologies);
        if (err is not null)
            logger.LogError("ARC-002 topology gate: {Error}", err);
        return err;
    }

    public string? ValidateCoupling(bool directDbCrossBoundary, bool domainCouplingAcrossContexts)
    {
        var err = ArcReferenceEvaluator.ValidateCoupling(
            directDbCrossBoundary, domainCouplingAcrossContexts, provider.Matrix.CouplingGuardrails);
        if (err is not null)
            logger.LogError("ARC-002 coupling gate: {Error}", err);
        return err;
    }

    public string? ValidateDataOwnership(bool singleWriterPresent)
    {
        var err = ArcReferenceEvaluator.ValidateDataOwnership(singleWriterPresent, provider.Matrix.DataOwnership);
        if (err is not null)
            logger.LogError("ARC-002 data-ownership gate: {Error}", err);
        return err;
    }

    public string? ValidateTenancy(bool tenantModelAligned)
    {
        var err = ArcReferenceEvaluator.ValidateTenancy(tenantModelAligned, provider.Matrix.TenancyBoundaries);
        if (err is not null)
            logger.LogError("ARC-002 tenancy gate: {Error}", err);
        return err;
    }

    public string? ValidatePlanes(bool planesSeparated)
    {
        var err = ArcReferenceEvaluator.ValidatePlanes(planesSeparated, provider.Matrix.ControlDataPlanes);
        if (err is not null)
            logger.LogError("ARC-002 planes gate: {Error}", err);
        return err;
    }

    public string? ValidateFitness(bool referenceImplementationPresent, bool labFitnessHarnessPresent)
    {
        var err = ArcReferenceEvaluator.ValidateFitness(
            referenceImplementationPresent, labFitnessHarnessPresent, provider.Matrix.FitnessEnvelope);
        if (err is not null)
            logger.LogError("ARC-002 fitness gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ArcReferenceEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("ARC-002 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ArcReferenceSigning? meta)
    {
        var err = ArcReferenceEvaluator.ValidateSigning(meta, provider.Matrix.ReferenceArchitecturePolicyVersion);
        if (err is not null)
            logger.LogError("ARC-002 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ArcReferenceStartupValidator(
    IArcReferenceProvider provider,
    IReferenceClaimGuard claimGuard,
    ArcReferenceMetrics metrics,
    IHostEnvironment environment,
    ILogger<ArcReferenceStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ArcReferenceEvaluator.Evaluate(provider.Matrix, opts.LiveReferenceClaim);

        var claimErr = claimGuard.EnsureLiveReferenceAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("ARC-002 reference rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "ARC-002 ArcReference startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "ARC-002: {Count} reference rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "ARC-002 arc reference active enforce={Enforce} liveReference={Claim} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveReferenceClaim,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
