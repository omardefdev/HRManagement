using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ExtPki;

public sealed class ExtPkiMetrics
{
    public const string MeterName = "HRManagement.ExtPki";

    private readonly IExtPkiProvider _provider;
    private long _auditFieldCount;
    private long _allowedAlgCount;
    private long _liveClaim;

    public ExtPkiMetrics(IExtPkiProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "ext_pki_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "EXT-004 audit required field count");
        meter.CreateObservableGauge(
            "ext_pki_allowed_alg_count",
            () => _allowedAlgCount,
            unit: "{algs}",
            description: "EXT-004 allowed algorithm count");
        meter.CreateObservableGauge(
            "ext_pki_live_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LivePkiClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _allowedAlgCount = _provider.Matrix.AlgorithmAgility?.AllowedAlgs.Count ?? 0;
        _liveClaim = _provider.Options.LivePkiClaim ? 1 : 0;
    }
}

public sealed class PkiSignatureClaimGuard(
    IExtPkiProvider provider,
    ILogger<PkiSignatureClaimGuard> logger) : IPkiSignatureClaimGuard
{
    public string? EnsureLivePkiAllowed()
    {
        if (!provider.Options.LivePkiClaim)
            return null;
        var err = "LivePkiClaim forbidden until EXT-004a-f PKI workers";
        logger.LogError("EXT-004 PKI claim: {Error}", err);
        return err;
    }

    public string? ValidateSignerIntent(bool intentPresent, bool rolePresent)
    {
        var err = ExtPkiEvaluator.ValidateSignerIntent(intentPresent, rolePresent, provider.Matrix);
        if (err is not null)
            logger.LogError("EXT-004 signer-intent gate: {Error}", err);
        return err;
    }

    public string? ValidateFormSignatureAsLegal(bool treatedAsLegal)
    {
        var err = ExtPkiEvaluator.ValidateFormSignatureAsLegal(treatedAsLegal, provider.Matrix.Signatures);
        if (err is not null)
            logger.LogError("EXT-004 form-signature gate: {Error}", err);
        return err;
    }

    public string? ValidateOrgSeal(bool cryptoSealPresent, bool metadataOnly)
    {
        var err = ExtPkiEvaluator.ValidateOrgSeal(
            cryptoSealPresent, metadataOnly, provider.Matrix.OrganizationalSeals);
        if (err is not null)
            logger.LogError("EXT-004 org-seal gate: {Error}", err);
        return err;
    }

    public string? ValidateRevocation(bool revoked, bool treatedAsValid)
    {
        var err = ExtPkiEvaluator.ValidateRevocation(revoked, treatedAsValid, provider.Matrix.Revocation);
        if (err is not null)
            logger.LogError("EXT-004 revocation gate: {Error}", err);
        return err;
    }

    public string? ValidateExpiredLtv(bool expired, bool hasLtv, bool treatedAsValid)
    {
        var err = ExtPkiEvaluator.ValidateExpiredLtv(expired, hasLtv, treatedAsValid, provider.Matrix.Ltv);
        if (err is not null)
            logger.LogError("EXT-004 LTV gate: {Error}", err);
        return err;
    }

    public string? ValidateClockSkew(int skewSeconds)
    {
        var err = ExtPkiEvaluator.ValidateClockSkew(skewSeconds, provider.Matrix.ClockSkewPolicy);
        if (err is not null)
            logger.LogError("EXT-004 clock gate: {Error}", err);
        return err;
    }

    public string? ValidateKeyExfil(bool keyLeftHsm)
    {
        var err = ExtPkiEvaluator.ValidateKeyExfil(keyLeftHsm, provider.Matrix.HsmCustody);
        if (err is not null)
            logger.LogError("EXT-004 HSM gate: {Error}", err);
        return err;
    }

    public string? ValidateAlgorithm(string alg)
    {
        var err = ExtPkiEvaluator.ValidateAlgorithm(alg, provider.Matrix.AlgorithmAgility);
        if (err is not null)
            logger.LogError("EXT-004 algorithm gate: {Error}", err);
        return err;
    }

    public string? ValidateTsaOutage(bool tsaAvailable, bool treatedAsValid)
    {
        var err = ExtPkiEvaluator.ValidateTsaOutage(
            tsaAvailable, treatedAsValid, provider.Matrix.TrustedTimestamp);
        if (err is not null)
            logger.LogError("EXT-004 TSA gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ExtPkiEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("EXT-004 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ExtPkiSigning? meta)
    {
        var err = ExtPkiEvaluator.ValidateSigning(meta, provider.Matrix.PkiPolicyVersion);
        if (err is not null)
            logger.LogError("EXT-004 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ExtPkiStartupValidator(
    IExtPkiProvider provider,
    IPkiSignatureClaimGuard claimGuard,
    ExtPkiMetrics metrics,
    IHostEnvironment environment,
    ILogger<ExtPkiStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ExtPkiEvaluator.Evaluate(provider.Matrix, opts.LivePkiClaim);

        var claimErr = claimGuard.EnsureLivePkiAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("EXT-004 PKI rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "EXT-004 ExtPki startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "EXT-004: {Count} PKI rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "EXT-004 ext pki active enforce={Enforce} livePki={Claim} allowedAlgs={Algs} auditFields={Audit} env={Env}",
            opts.EnforceOnStartup,
            opts.LivePkiClaim,
            provider.Matrix.AlgorithmAgility?.AllowedAlgs.Count ?? 0,
            provider.Matrix.Audit?.RequiredFields.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
