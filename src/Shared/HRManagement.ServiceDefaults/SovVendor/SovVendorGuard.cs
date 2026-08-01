using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.SovVendor;

public sealed class SovVendorMetrics
{
    public const string MeterName = "HRManagement.SovVendor";

    private readonly ISovVendorProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;

    public SovVendorMetrics(ISovVendorProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "sov_vendor_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "SOV-007 audit required field count");
        meter.CreateObservableGauge(
            "sov_vendor_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "SOV-007 core policy flags asserted");
        meter.CreateObservableGauge(
            "sov_vendor_live_vendor_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveVendorClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveVendorClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.Ownership?.IpSourceBuildIacSbomDocsRequired == true ? 1 : 0) +
            (m.EscrowLicenses?.EscrowRequired == true ? 1 : 0) +
            (m.OpenExport?.FullDataExportRequired == true ? 1 : 0) +
            (m.CriticalAlternates?.NoCriticalWithoutAlternateOrException == true ? 1 : 0) +
            (m.CleanRoomRebuild?.OwnedArtifactsOnlyRequired == true ? 1 : 0) +
            (m.DestructionAttestation?.PriorVendorProofRequired == true ? 1 : 0) +
            (m.MockSandbox?.LiveCleanRoomRebuildForbidden == true ? 1 : 0);
    }
}

public sealed class VendorClaimGuard(
    ISovVendorProvider provider,
    ILogger<VendorClaimGuard> logger) : IVendorClaimGuard
{
    public string? EnsureLiveVendorAllowed()
    {
        if (!provider.Options.LiveVendorClaim)
            return null;
        var err = "LiveVendorClaim forbidden until SOV-007a live clean-room rebuild";
        logger.LogError("SOV-007 vendor claim: {Error}", err);
        return err;
    }

    public string? ValidateOwnership(bool ownedArtifactsPresent)
    {
        var err = SovVendorEvaluator.ValidateOwnership(ownedArtifactsPresent, provider.Matrix.Ownership);
        if (err is not null)
            logger.LogError("SOV-007 ownership gate: {Error}", err);
        return err;
    }

    public string? ValidateOpenExport(bool fullExportPresent, bool formatsDocumented)
    {
        var err = SovVendorEvaluator.ValidateOpenExport(fullExportPresent, formatsDocumented, provider.Matrix.OpenExport);
        if (err is not null)
            logger.LogError("SOV-007 export gate: {Error}", err);
        return err;
    }

    public string? ValidateCriticalAlternates(bool criticalWithoutAlternate)
    {
        var err = SovVendorEvaluator.ValidateCriticalAlternates(criticalWithoutAlternate, provider.Matrix.CriticalAlternates);
        if (err is not null)
            logger.LogError("SOV-007 alternates gate: {Error}", err);
        return err;
    }

    public string? ValidateCleanRoom(bool ownedArtifactsOnly, bool alternateSitePresent)
    {
        var err = SovVendorEvaluator.ValidateCleanRoom(ownedArtifactsOnly, alternateSitePresent, provider.Matrix.CleanRoomRebuild);
        if (err is not null)
            logger.LogError("SOV-007 clean-room gate: {Error}", err);
        return err;
    }

    public string? ValidateEscrow(bool escrowPresent, bool licenseRightsPresent)
    {
        var err = SovVendorEvaluator.ValidateEscrow(escrowPresent, licenseRightsPresent, provider.Matrix.EscrowLicenses);
        if (err is not null)
            logger.LogError("SOV-007 escrow gate: {Error}", err);
        return err;
    }

    public string? ValidateDestruction(bool attested)
    {
        var err = SovVendorEvaluator.ValidateDestruction(attested, provider.Matrix.DestructionAttestation);
        if (err is not null)
            logger.LogError("SOV-007 destruction gate: {Error}", err);
        return err;
    }

    public string? ValidateKnowledgeTransfer(bool transferPresent)
    {
        var err = SovVendorEvaluator.ValidateKnowledgeTransfer(transferPresent, provider.Matrix.KnowledgeTransfer);
        if (err is not null)
            logger.LogError("SOV-007 knowledge gate: {Error}", err);
        return err;
    }

    public string? ValidateExitAssistance(bool assistancePresent)
    {
        var err = SovVendorEvaluator.ValidateExitAssistance(assistancePresent, provider.Matrix.ExitAssistance);
        if (err is not null)
            logger.LogError("SOV-007 exit-assistance gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = SovVendorEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("SOV-007 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(SovVendorSigning? meta)
    {
        var err = SovVendorEvaluator.ValidateSigning(meta, provider.Matrix.VendorExitPolicyVersion);
        if (err is not null)
            logger.LogError("SOV-007 signing gate: {Error}", err);
        return err;
    }
}

public sealed class SovVendorStartupValidator(
    ISovVendorProvider provider,
    IVendorClaimGuard claimGuard,
    SovVendorMetrics metrics,
    IHostEnvironment environment,
    ILogger<SovVendorStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = SovVendorEvaluator.Evaluate(provider.Matrix, opts.LiveVendorClaim);

        var claimErr = claimGuard.EnsureLiveVendorAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("SOV-007 vendor-exit rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "SOV-007 SovVendor startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "SOV-007: {Count} vendor-exit rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "SOV-007 sov vendor active enforce={Enforce} liveVendor={Claim} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveVendorClaim,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
