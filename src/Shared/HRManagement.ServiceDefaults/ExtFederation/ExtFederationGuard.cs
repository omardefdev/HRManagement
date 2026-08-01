using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ExtFederation;

public sealed class ExtFederationMetrics
{
    public const string MeterName = "HRManagement.ExtFederation";

    private readonly IExtFederationProvider _provider;
    private long _personaCount;
    private long _agencyIdpCount;
    private long _liveClaim;

    public ExtFederationMetrics(IExtFederationProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "ext_federation_persona_count",
            () => _personaCount,
            unit: "{personas}",
            description: "EXT-001 persona count");
        meter.CreateObservableGauge(
            "ext_federation_agency_idp_count",
            () => _agencyIdpCount,
            unit: "{idps}",
            description: "EXT-001 agency IdP stub count");
        meter.CreateObservableGauge(
            "ext_federation_live_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveFederationClaim is true");
    }

    public void Recount()
    {
        _personaCount = _provider.Matrix.Personas.Count;
        _agencyIdpCount = _provider.Matrix.FederationTrust?.AgencyIdps.Count ?? 0;
        _liveClaim = _provider.Options.LiveFederationClaim ? 1 : 0;
    }
}

public sealed class FederationAssuranceClaimGuard(
    IExtFederationProvider provider,
    ILogger<FederationAssuranceClaimGuard> logger) : IFederationAssuranceClaimGuard
{
    public string? EnsureLiveFederationAllowed()
    {
        if (!provider.Options.LiveFederationClaim)
            return null;
        var err = "LiveFederationClaim forbidden until EXT-001a-d federation workers";
        logger.LogError("EXT-001 federation claim: {Error}", err);
        return err;
    }

    public string? ValidatePersonaAssurance(string personaId, string aal)
    {
        var err = ExtFederationEvaluator.ValidatePersonaAssurance(personaId, aal, provider.Matrix.Personas);
        if (err is not null)
            logger.LogError("EXT-001 persona gate: {Error}", err);
        return err;
    }

    public string? ValidateNameOnlyLink(string linkKey)
    {
        var err = ExtFederationEvaluator.ValidateNameOnlyLink(linkKey, provider.Matrix.AttributeAuthority);
        if (err is not null)
            logger.LogError("EXT-001 attribute gate: {Error}", err);
        return err;
    }

    public string? ValidateRevocationSla(int elapsedMinutes, bool escalated)
    {
        var err = ExtFederationEvaluator.ValidateRevocationSla(
            elapsedMinutes, escalated, provider.Matrix.Revocation);
        if (err is not null)
            logger.LogError("EXT-001 revocation gate: {Error}", err);
        return err;
    }

    public string? ValidateLiveIdpClaim(bool nationalLive, bool agencyLive)
    {
        var err = ExtFederationEvaluator.ValidateLiveIdpClaim(
            nationalLive, agencyLive, provider.Matrix.FederationTrust);
        if (err is not null)
            logger.LogError("EXT-001 IdP live gate: {Error}", err);
        return err;
    }

    public string? ValidateSmsOtpForSensitive(string role, bool usedSmsOtp)
    {
        var err = ExtFederationEvaluator.ValidateSmsOtpForSensitive(
            role, usedSmsOtp, provider.Matrix.MfaPolicy);
        if (err is not null)
            logger.LogError("EXT-001 MFA gate: {Error}", err);
        return err;
    }

    public string? ValidateCitizenEmployeeMix(bool sharedPrincipal)
    {
        var err = ExtFederationEvaluator.ValidateCitizenEmployeeMix(
            sharedPrincipal, provider.Matrix.CitizenEmployeeSeparation);
        if (err is not null)
            logger.LogError("EXT-001 separation gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ExtFederationSigning? meta)
    {
        var err = ExtFederationEvaluator.ValidateSigning(meta, provider.Matrix.AssurancePolicyVersion);
        if (err is not null)
            logger.LogError("EXT-001 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ExtFederationStartupValidator(
    IExtFederationProvider provider,
    IFederationAssuranceClaimGuard claimGuard,
    ExtFederationMetrics metrics,
    IHostEnvironment environment,
    ILogger<ExtFederationStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ExtFederationEvaluator.Evaluate(provider.Matrix, opts.LiveFederationClaim);

        var claimErr = claimGuard.EnsureLiveFederationAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("EXT-001 federation rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "EXT-001 ExtFederation startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "EXT-001: {Count} federation rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "EXT-001 ext federation active enforce={Enforce} liveFed={Claim} personas={Personas} agencyIdps={Idps} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveFederationClaim,
            provider.Matrix.Personas.Count,
            provider.Matrix.FederationTrust?.AgencyIdps.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
