using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ProdAgency;

public sealed class ProdAgencyMetrics
{
    public const string MeterName = "HRManagement.ProdAgency";

    private readonly IProdAgencyProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;
    private long _scopeCount;

    public ProdAgencyMetrics(IProdAgencyProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "prod_agency_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "PROD-002 audit required field count");
        meter.CreateObservableGauge(
            "prod_agency_scope_count",
            () => _scopeCount,
            unit: "{scopes}",
            description: "PROD-002 delegated scope count");
        meter.CreateObservableGauge(
            "prod_agency_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "PROD-002 core policy flags asserted");
        meter.CreateObservableGauge(
            "prod_agency_live_selfadmin_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveAgencySelfAdminClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _scopeCount = _provider.Matrix.DelegatedAdmin?.Scopes.Count ?? 0;
        _liveClaim = _provider.Options.LiveAgencySelfAdminClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.NationalControlsImmutable ? 1 : 0) +
            (m.CrossTenantAdminForbidden ? 1 : 0) +
            (m.DelegatedAdmin?.NoDbaDeveloperRequired == true ? 1 : 0) +
            (m.FourEyesSensitive?.Required == true ? 1 : 0) +
            (m.MockSandbox?.LiveSelfAdminForbidden == true ? 1 : 0);
    }
}

public sealed class AgencySelfAdminClaimGuard(
    IProdAgencyProvider provider,
    ILogger<AgencySelfAdminClaimGuard> logger) : IAgencySelfAdminClaimGuard
{
    public string? EnsureLiveSelfAdminAllowed()
    {
        if (!provider.Options.LiveAgencySelfAdminClaim)
            return null;
        var err = "LiveAgencySelfAdminClaim forbidden until PROD-002a-e agency selfadmin workers";
        logger.LogError("PROD-002 selfadmin claim: {Error}", err);
        return err;
    }

    public string? ValidateDelegatedScope(bool scopeAllowed, bool noDbaPath)
    {
        var err = ProdAgencyEvaluator.ValidateDelegatedScope(
            scopeAllowed, noDbaPath, provider.Matrix.DelegatedAdmin);
        if (err is not null)
            logger.LogError("PROD-002 delegated-scope gate: {Error}", err);
        return err;
    }

    public string? ValidateNationalControlsImmutable(bool attemptedNationalMutate)
    {
        var err = ProdAgencyEvaluator.ValidateNationalControlsImmutable(
            attemptedNationalMutate, provider.Matrix.NationalControlsImmutable);
        if (err is not null)
            logger.LogError("PROD-002 national-ceiling gate: {Error}", err);
        return err;
    }

    public string? ValidateCrossTenantForbidden(bool targetTenantEqualsActorTenant)
    {
        var err = ProdAgencyEvaluator.ValidateCrossTenantForbidden(
            targetTenantEqualsActorTenant, provider.Matrix.CrossTenantAdminForbidden);
        if (err is not null)
            logger.LogError("PROD-002 cross-tenant gate: {Error}", err);
        return err;
    }

    public string? ValidateFourEyesSensitive(bool isSensitive, string? publisherRole, string? reviewerRole)
    {
        var err = ProdAgencyEvaluator.ValidateFourEyesSensitive(
            isSensitive, publisherRole, reviewerRole, provider.Matrix.FourEyesSensitive);
        if (err is not null)
            logger.LogError("PROD-002 four-eyes gate: {Error}", err);
        return err;
    }

    public string? ValidateRollback(bool hasRollbackRef)
    {
        var err = ProdAgencyEvaluator.ValidateRollback(hasRollbackRef, provider.Matrix.Rollback);
        if (err is not null)
            logger.LogError("PROD-002 rollback gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ProdAgencyEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("PROD-002 audit gate: {Error}", err);
        return err;
    }

    public string? ValidatePreview(bool previewUsed)
    {
        var err = ProdAgencyEvaluator.ValidatePreview(previewUsed, provider.Matrix.Preview);
        if (err is not null)
            logger.LogError("PROD-002 preview gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ProdAgencySigning? meta)
    {
        var err = ProdAgencyEvaluator.ValidateSigning(meta, provider.Matrix.AgencySelfAdminPolicyVersion);
        if (err is not null)
            logger.LogError("PROD-002 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ProdAgencyStartupValidator(
    IProdAgencyProvider provider,
    IAgencySelfAdminClaimGuard claimGuard,
    ProdAgencyMetrics metrics,
    IHostEnvironment environment,
    ILogger<ProdAgencyStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ProdAgencyEvaluator.Evaluate(provider.Matrix, opts.LiveAgencySelfAdminClaim);

        var claimErr = claimGuard.EnsureLiveSelfAdminAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("PROD-002 agency-selfadmin rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "PROD-002 ProdAgency startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "PROD-002: {Count} agency-selfadmin rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "PROD-002 prod agency active enforce={Enforce} liveSelfAdmin={Claim} scopes={Scopes} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveAgencySelfAdminClaim,
            provider.Matrix.DelegatedAdmin?.Scopes.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
