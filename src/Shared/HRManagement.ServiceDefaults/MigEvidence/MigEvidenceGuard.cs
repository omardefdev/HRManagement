using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.MigEvidence;

public sealed class MigEvidenceMetrics
{
    public const string MeterName = "HRManagement.MigEvidence";

    private readonly IMigEvidenceProvider _provider;
    private long _bucketCount;
    private long _artifactCount;
    private long _liveClaim;

    public MigEvidenceMetrics(IMigEvidenceProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "mig_evidence_bucket_count",
            () => _bucketCount,
            unit: "{buckets}",
            description: "MIG-005 accounted bucket count");
        meter.CreateObservableGauge(
            "mig_evidence_artifact_count",
            () => _artifactCount,
            unit: "{artifacts}",
            description: "MIG-005 required evidence artifact count");
        meter.CreateObservableGauge(
            "mig_evidence_live_recon_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveAgencyReconClaim is true");
    }

    public void Recount()
    {
        _bucketCount = _provider.Matrix.AccountedBuckets.Count;
        _artifactCount = _provider.Matrix.EvidencePackage?.RequiredArtifacts.Count ?? 0;
        _liveClaim = _provider.Options.LiveAgencyReconClaim ? 1 : 0;
    }
}

public sealed class AgencyReconClaimGuard(
    IMigEvidenceProvider provider,
    ILogger<AgencyReconClaimGuard> logger) : IAgencyReconClaimGuard
{
    public string? EnsureLiveAgencyReconAllowed()
    {
        if (!provider.Options.LiveAgencyReconClaim)
            return null;
        var err = "LiveAgencyReconClaim forbidden until MIG-005a-d recon workers";
        logger.LogError("MIG-005 agency recon claim: {Error}", err);
        return err;
    }

    public string? ValidateAccountedBreakdown(
        int migrated, int rejected, int deferred, int sourceTotal,
        bool rejectedReasonsPresent, bool deferredReasonsPresent)
    {
        var err = MigEvidenceEvaluator.ValidateAccountedBreakdown(
            migrated, rejected, deferred, sourceTotal,
            rejectedReasonsPresent, deferredReasonsPresent, provider.Matrix);
        if (err is not null)
            logger.LogError("MIG-005 accounted gate: {Error}", err);
        return err;
    }

    public string? ValidateFinancialVariance(double unexplainedAmount)
    {
        var err = MigEvidenceEvaluator.ValidateFinancialVariance(unexplainedAmount, provider.Matrix.FinancialPolicy);
        if (err is not null)
            logger.LogError("MIG-005 financial gate: {Error}", err);
        return err;
    }

    public string? ValidateDualControl(IReadOnlyList<(string Role, string Actor)> signers)
    {
        var err = MigEvidenceEvaluator.ValidateDualControlSigners(signers, provider.Matrix.DualControl);
        if (err is not null)
            logger.LogError("MIG-005 dual control gate: {Error}", err);
        return err;
    }

    public string? ValidateEvidenceImmutability(bool claimedImmutable, bool hashMatch)
    {
        var err = MigEvidenceEvaluator.ValidateEvidenceImmutability(claimedImmutable, hashMatch);
        if (err is not null)
            logger.LogError("MIG-005 evidence immutability gate: {Error}", err);
        return err;
    }

    public string? ValidateIndependentSqlClaim(bool liveIndependentSql)
    {
        var err = MigEvidenceEvaluator.ValidateIndependentSqlClaim(liveIndependentSql, provider.Matrix);
        if (err is not null)
            logger.LogError("MIG-005 independent SQL gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(MigEvidenceSigning? meta)
    {
        var err = MigEvidenceEvaluator.ValidateSigning(meta, provider.Matrix.EvidencePolicyVersion);
        if (err is not null)
            logger.LogError("MIG-005 signing gate: {Error}", err);
        return err;
    }
}

public sealed class MigEvidenceStartupValidator(
    IMigEvidenceProvider provider,
    IAgencyReconClaimGuard claimGuard,
    MigEvidenceMetrics metrics,
    IHostEnvironment environment,
    ILogger<MigEvidenceStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = MigEvidenceEvaluator.Evaluate(provider.Matrix, opts.LiveAgencyReconClaim);

        var claimErr = claimGuard.EnsureLiveAgencyReconAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("MIG-005 evidence rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "MIG-005 MigEvidence startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "MIG-005: {Count} evidence rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "MIG-005 mig evidence active enforce={Enforce} liveRecon={Claim} buckets={Buckets} artifacts={Artifacts} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveAgencyReconClaim,
            provider.Matrix.AccountedBuckets.Count,
            provider.Matrix.EvidencePackage?.RequiredArtifacts.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
