using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ExtCivil;

public sealed class ExtCivilMetrics
{
    public const string MeterName = "HRManagement.ExtCivil";

    private readonly IExtCivilProvider _provider;
    private long _purposeCount;
    private long _auditFieldCount;
    private long _liveClaim;

    public ExtCivilMetrics(IExtCivilProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "ext_civil_purpose_count",
            () => _purposeCount,
            unit: "{purposes}",
            description: "EXT-002 purpose allowlist count");
        meter.CreateObservableGauge(
            "ext_civil_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "EXT-002 audit required field count");
        meter.CreateObservableGauge(
            "ext_civil_live_moi_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveMoiClaim is true");
    }

    public void Recount()
    {
        _purposeCount = _provider.Matrix.PurposeAllowlist.Count;
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveMoiClaim ? 1 : 0;
    }
}

public sealed class CivilRegistryClaimGuard(
    IExtCivilProvider provider,
    ILogger<CivilRegistryClaimGuard> logger) : ICivilRegistryClaimGuard
{
    public string? EnsureLiveMoiAllowed()
    {
        if (!provider.Options.LiveMoiClaim)
            return null;
        var err = "LiveMoiClaim forbidden until EXT-002a-d civil registry workers";
        logger.LogError("EXT-002 MOI claim: {Error}", err);
        return err;
    }

    public string? ValidatePurpose(string purposeCode)
    {
        var err = ExtCivilEvaluator.ValidatePurpose(purposeCode, provider.Matrix.PurposeAllowlist);
        if (err is not null)
            logger.LogError("EXT-002 purpose gate: {Error}", err);
        return err;
    }

    public string? ValidateConsent(bool consentPresent)
    {
        var err = ExtCivilEvaluator.ValidateConsent(consentPresent, provider.Matrix.ConsentRequired);
        if (err is not null)
            logger.LogError("EXT-002 consent gate: {Error}", err);
        return err;
    }

    public string? ValidateStoredFields(IEnumerable<string> fields)
    {
        var err = ExtCivilEvaluator.ValidateStoredFields(fields, provider.Matrix.MinimalQuery);
        if (err is not null)
            logger.LogError("EXT-002 minimal field gate: {Error}", err);
        return err;
    }

    public string? ValidateOutageAsTruth(bool registryAvailable, bool treatedAsVerified)
    {
        var err = ExtCivilEvaluator.ValidateOutageAsTruth(
            registryAvailable, treatedAsVerified, provider.Matrix.UnavailableMode);
        if (err is not null)
            logger.LogError("EXT-002 unavailable gate: {Error}", err);
        return err;
    }

    public string? ValidateStaleAsTruth(bool stale, bool usedAsTruth)
    {
        var err = ExtCivilEvaluator.ValidateStaleAsTruth(stale, usedAsTruth, provider.Matrix.Caching);
        if (err is not null)
            logger.LogError("EXT-002 cache gate: {Error}", err);
        return err;
    }

    public string? ValidateBulkHarvest(bool attempted)
    {
        var err = ExtCivilEvaluator.ValidateBulkHarvest(attempted, provider.Matrix.RateLimits);
        if (err is not null)
            logger.LogError("EXT-002 rate gate: {Error}", err);
        return err;
    }

    public string? ValidateMismatchWorkflow(bool isMismatch, bool humanQueued)
    {
        var err = ExtCivilEvaluator.ValidateMismatchWorkflow(
            isMismatch, humanQueued, provider.Matrix.MismatchHumanWorkflow);
        if (err is not null)
            logger.LogError("EXT-002 mismatch gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ExtCivilEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("EXT-002 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ExtCivilSigning? meta)
    {
        var err = ExtCivilEvaluator.ValidateSigning(meta, provider.Matrix.CivilRegistryPolicyVersion);
        if (err is not null)
            logger.LogError("EXT-002 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ExtCivilStartupValidator(
    IExtCivilProvider provider,
    ICivilRegistryClaimGuard claimGuard,
    ExtCivilMetrics metrics,
    IHostEnvironment environment,
    ILogger<ExtCivilStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ExtCivilEvaluator.Evaluate(provider.Matrix, opts.LiveMoiClaim);

        var claimErr = claimGuard.EnsureLiveMoiAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("EXT-002 civil registry rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "EXT-002 ExtCivil startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "EXT-002: {Count} civil registry rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "EXT-002 ext civil active enforce={Enforce} liveMoi={Claim} purposes={Purposes} auditFields={Audit} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveMoiClaim,
            provider.Matrix.PurposeAllowlist.Count,
            provider.Matrix.Audit?.RequiredFields.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
