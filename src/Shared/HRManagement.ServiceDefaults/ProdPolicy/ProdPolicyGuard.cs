using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ProdPolicy;

public sealed class ProdPolicyMetrics
{
    public const string MeterName = "HRManagement.ProdPolicy";

    private readonly IProdPolicyProvider _provider;
    private long _auditFieldCount;
    private long _liveClaim;
    private long _policyFlags;
    private long _kindCount;

    public ProdPolicyMetrics(IProdPolicyProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "prod_policy_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "PROD-001 audit required field count");
        meter.CreateObservableGauge(
            "prod_policy_kind_count",
            () => _kindCount,
            unit: "{kinds}",
            description: "PROD-001 template kind count");
        meter.CreateObservableGauge(
            "prod_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "PROD-001 core policy flags asserted");
        meter.CreateObservableGauge(
            "prod_policy_live_engine_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LivePolicyEngineClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _kindCount = _provider.Matrix.VersionedTemplates?.Kinds.Count ?? 0;
        _liveClaim = _provider.Options.LivePolicyEngineClaim ? 1 : 0;
        var m = _provider.Matrix;
        _policyFlags =
            (m.NoArbitraryCode ? 1 : 0) +
            (m.InvalidPublishForbidden ? 1 : 0) +
            (m.ChangeWithoutDeployOrFork ? 1 : 0) +
            (m.Simulation?.SyntheticOnly == true ? 1 : 0) +
            (m.MockSandbox?.LiveEngineForbidden == true ? 1 : 0);
    }
}

public sealed class PolicyPlatformClaimGuard(
    IProdPolicyProvider provider,
    ILogger<PolicyPlatformClaimGuard> logger) : IPolicyPlatformClaimGuard
{
    public string? EnsureLiveEngineAllowed()
    {
        if (!provider.Options.LivePolicyEngineClaim)
            return null;

        var engine = provider.Matrix.Blockers.FirstOrDefault(b =>
            string.Equals(b.Id, "live-policy-engine", StringComparison.OrdinalIgnoreCase));
        var workflow = provider.Matrix.Blockers.FirstOrDefault(b =>
            string.Equals(b.Id, "live-workflow-runtime", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(engine?.Status, "Satisfied", StringComparison.OrdinalIgnoreCase)
            && string.Equals(workflow?.Status, "Satisfied", StringComparison.OrdinalIgnoreCase))
            return null;

        const string err = "LivePolicyEngineClaim requires live-policy-engine and live-workflow-runtime Satisfied";
        logger.LogError("PROD-001 engine claim: {Error}", err);
        return err;
    }

    public string? ValidateVersionedTemplate(bool hasVersion, string? kind)
    {
        var err = ProdPolicyEvaluator.ValidateVersionedTemplate(hasVersion, kind, provider.Matrix.VersionedTemplates);
        if (err is not null)
            logger.LogError("PROD-001 version gate: {Error}", err);
        return err;
    }

    public string? ValidateHistoricalPin(string? asOf, bool resolvedVersionMatchesPin)
    {
        _ = asOf;
        var err = ProdPolicyEvaluator.ValidateHistoricalPin(resolvedVersionMatchesPin, provider.Matrix.EffectiveDating);
        if (err is not null)
            logger.LogError("PROD-001 historical-pin gate: {Error}", err);
        return err;
    }

    public string? ValidateFourEyesPublish(string? publisherRole, string? reviewerRole)
    {
        var err = ProdPolicyEvaluator.ValidateFourEyesPublish(
            publisherRole, reviewerRole, provider.Matrix.FourEyesPublish);
        if (err is not null)
            logger.LogError("PROD-001 four-eyes gate: {Error}", err);
        return err;
    }

    public string? ValidateSimulationSynthetic(bool usesRealData)
    {
        var err = ProdPolicyEvaluator.ValidateSimulationSynthetic(usesRealData, provider.Matrix.Simulation);
        if (err is not null)
            logger.LogError("PROD-001 simulation gate: {Error}", err);
        return err;
    }

    public string? ValidateInvalidPublish(bool isValid, bool published)
    {
        var err = ProdPolicyEvaluator.ValidateInvalidPublish(
            isValid, published, provider.Matrix.InvalidPublishForbidden);
        if (err is not null)
            logger.LogError("PROD-001 publish gate: {Error}", err);
        return err;
    }

    public string? ValidateOverrideCeiling(bool overrideTouchesNationalMandatory)
    {
        var err = ProdPolicyEvaluator.ValidateOverrideCeiling(
            overrideTouchesNationalMandatory, provider.Matrix.TenantOverride);
        if (err is not null)
            logger.LogError("PROD-001 override gate: {Error}", err);
        return err;
    }

    public string? ValidateNoArbitraryCode(bool containsExecutableCode)
    {
        var err = ProdPolicyEvaluator.ValidateNoArbitraryCode(
            containsExecutableCode, provider.Matrix.NoArbitraryCode);
        if (err is not null)
            logger.LogError("PROD-001 code gate: {Error}", err);
        return err;
    }

    public string? ValidateRollback(bool hasRollbackRef)
    {
        var err = ProdPolicyEvaluator.ValidateRollback(hasRollbackRef, provider.Matrix.Rollback);
        if (err is not null)
            logger.LogError("PROD-001 rollback gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ProdPolicyEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("PROD-001 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ProdPolicySigning? meta)
    {
        var err = ProdPolicyEvaluator.ValidateSigning(meta, provider.Matrix.PolicyPlatformPolicyVersion);
        if (err is not null)
            logger.LogError("PROD-001 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ProdPolicyStartupValidator(
    IProdPolicyProvider provider,
    IPolicyPlatformClaimGuard claimGuard,
    ProdPolicyMetrics metrics,
    IHostEnvironment environment,
    ILogger<ProdPolicyStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ProdPolicyEvaluator.Evaluate(provider.Matrix, opts.LivePolicyEngineClaim);

        var claimErr = claimGuard.EnsureLiveEngineAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("PROD-001 policy-platform rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "PROD-001 ProdPolicy startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "PROD-001: {Count} policy-platform rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "PROD-001 prod policy active enforce={Enforce} liveEngine={Claim} kinds={Kinds} env={Env}",
            opts.EnforceOnStartup,
            opts.LivePolicyEngineClaim,
            provider.Matrix.VersionedTemplates?.Kinds.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
