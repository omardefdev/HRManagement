using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ExtSandbox;

public sealed class ExtSandboxMetrics
{
    public const string MeterName = "HRManagement.ExtSandbox";

    private readonly IExtSandboxProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;

    public ExtSandboxMetrics(IExtSandboxProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "ext_sandbox_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "EXT-006 audit required field count");
        meter.CreateObservableGauge(
            "ext_sandbox_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "EXT-006 core policy flags asserted");
        meter.CreateObservableGauge(
            "ext_sandbox_live_portal_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveSandboxPortalClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveSandboxPortalClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.SyntheticDataOnly ? 1 : 0) +
            (m.RealPiiForbidden ? 1 : 0) +
            (m.DocsMatchPublishedContract ? 1 : 0) +
            (m.Credentials?.NoProdAccess == true ? 1 : 0) +
            (m.Conformance?.IncompatibleClientMustFail == true ? 1 : 0) +
            (m.MockSandbox?.LivePortalForbidden == true ? 1 : 0);
    }
}

public sealed class SandboxCertClaimGuard(
    IExtSandboxProvider provider,
    ILogger<SandboxCertClaimGuard> logger) : ISandboxCertClaimGuard
{
    public string? EnsureLivePortalAllowed()
    {
        if (!provider.Options.LiveSandboxPortalClaim)
            return null;
        var err = "LiveSandboxPortalClaim forbidden until EXT-006a-e sandbox portal workers";
        logger.LogError("EXT-006 portal claim: {Error}", err);
        return err;
    }

    public string? ValidateNoProdAccess(bool attemptedProd)
    {
        var err = ExtSandboxEvaluator.ValidateNoProdAccess(attemptedProd, provider.Matrix.Credentials);
        if (err is not null)
            logger.LogError("EXT-006 prod-access gate: {Error}", err);
        return err;
    }

    public string? ValidateNoRealPii(bool containsRealPii)
    {
        var err = ExtSandboxEvaluator.ValidateNoRealPii(containsRealPii, provider.Matrix.RealPiiForbidden);
        if (err is not null)
            logger.LogError("EXT-006 real-pii gate: {Error}", err);
        return err;
    }

    public string? ValidateEphemeralCredential(bool isEphemeral, bool isManualSecret)
    {
        var err = ExtSandboxEvaluator.ValidateEphemeralCredential(
            isEphemeral, isManualSecret, provider.Matrix.Credentials);
        if (err is not null)
            logger.LogError("EXT-006 credential gate: {Error}", err);
        return err;
    }

    public string? ValidateIncompatibleClient(bool treatedAsCertified, bool suitePassed)
    {
        var err = ExtSandboxEvaluator.ValidateIncompatibleClient(
            treatedAsCertified, suitePassed, provider.Matrix.Conformance);
        if (err is not null)
            logger.LogError("EXT-006 conformance gate: {Error}", err);
        return err;
    }

    public string? ValidateDocsMatch(bool docsMatchContract)
    {
        var err = ExtSandboxEvaluator.ValidateDocsMatch(
            docsMatchContract, provider.Matrix.DocsMatchPublishedContract);
        if (err is not null)
            logger.LogError("EXT-006 docs gate: {Error}", err);
        return err;
    }

    public string? ValidateRevocation(bool compromised, bool revoked)
    {
        var err = ExtSandboxEvaluator.ValidateRevocation(
            compromised, revoked, provider.Matrix.Certification);
        if (err is not null)
            logger.LogError("EXT-006 revocation gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ExtSandboxEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("EXT-006 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ExtSandboxSigning? meta)
    {
        var err = ExtSandboxEvaluator.ValidateSigning(meta, provider.Matrix.SandboxCertPolicyVersion);
        if (err is not null)
            logger.LogError("EXT-006 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ExtSandboxStartupValidator(
    IExtSandboxProvider provider,
    ISandboxCertClaimGuard claimGuard,
    ExtSandboxMetrics metrics,
    IHostEnvironment environment,
    ILogger<ExtSandboxStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ExtSandboxEvaluator.Evaluate(provider.Matrix, opts.LiveSandboxPortalClaim);

        var claimErr = claimGuard.EnsureLivePortalAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("EXT-006 sandbox-cert rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "EXT-006 ExtSandbox startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "EXT-006: {Count} sandbox-cert rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "EXT-006 ext sandbox active enforce={Enforce} livePortal={Claim} auditFields={Audit} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveSandboxPortalClaim,
            provider.Matrix.Audit?.RequiredFields.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
