using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.ProdSemantic;

public sealed class ProdSemanticMetrics
{
    public const string MeterName = "HRManagement.ProdSemantic";

    private readonly IProdSemanticProvider _provider;
    private long _auditFieldCount;
    private long _kpiFlags;
    private long _liveClaim;
    private long _policyFlags;

    public ProdSemanticMetrics(IProdSemanticProvider provider, IMeterFactory? meterFactory = null)
    {
        _provider = provider;
        Recount();
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        meter.CreateObservableGauge(
            "prod_semantic_audit_field_count",
            () => _auditFieldCount,
            unit: "{fields}",
            description: "PROD-006 audit required field count");
        meter.CreateObservableGauge(
            "prod_semantic_kpi_flag_count",
            () => _kpiFlags,
            unit: "{flags}",
            description: "PROD-006 KPI/policy flags asserted");
        meter.CreateObservableGauge(
            "prod_semantic_policy_flag_count",
            () => _policyFlags,
            unit: "{flags}",
            description: "PROD-006 core privacy/RLS/live-forbid flags");
        meter.CreateObservableGauge(
            "prod_semantic_live_semantic_layer_claim",
            () => _liveClaim,
            unit: "{bool}",
            description: "1 when LiveSemanticLayerClaim is true");
    }

    public void Recount()
    {
        _auditFieldCount = _provider.Matrix.Audit?.RequiredFields.Count ?? 0;
        _liveClaim = _provider.Options.LiveSemanticLayerClaim ? 1 : 0;
        var m = _provider.Matrix;
        _kpiFlags =
            (m.SemanticMetrics?.UnifiedDefinitionRequired == true ? 1 : 0) +
            (m.Lineage?.VersionAsOfSourceRequired == true ? 1 : 0) +
            (m.Freshness?.Required == true ? 1 : 0);
        _policyFlags =
            (m.TenantRegionRollups?.CrossTenantForbidden == true ? 1 : 0) +
            (m.PrivacyThresholds?.SmallCellSuppressionRequired == true ? 1 : 0) +
            (m.GovernedBi?.LiveDashboardForbidden == true ? 1 : 0) +
            (m.MockSandbox?.LiveWarehouseForbidden == true ? 1 : 0) +
            (m.MockSandbox?.LiveBiDashboardForbidden == true ? 1 : 0);
    }
}

public sealed class SemanticLayerClaimGuard(
    IProdSemanticProvider provider,
    ILogger<SemanticLayerClaimGuard> logger) : ISemanticLayerClaimGuard
{
    public string? EnsureLiveSemanticAllowed()
    {
        if (!provider.Options.LiveSemanticLayerClaim)
            return null;
        var err = "LiveSemanticLayerClaim forbidden until PROD-006a-e live semantic workers";
        logger.LogError("PROD-006 semantic claim: {Error}", err);
        return err;
    }

    public string? ValidateUnifiedKpiDefinition(bool definitionPresent)
    {
        var err = ProdSemanticEvaluator.ValidateUnifiedKpiDefinition(
            definitionPresent, provider.Matrix.SemanticMetrics);
        if (err is not null)
            logger.LogError("PROD-006 KPI gate: {Error}", err);
        return err;
    }

    public string? ValidateReconciliation(bool reconciledWithSource)
    {
        var err = ProdSemanticEvaluator.ValidateReconciliation(reconciledWithSource);
        if (err is not null)
            logger.LogError("PROD-006 reconciliation gate: {Error}", err);
        return err;
    }

    public string? ValidateTenantRls(bool withinTenant)
    {
        var err = ProdSemanticEvaluator.ValidateTenantRls(withinTenant, provider.Matrix.TenantRegionRollups);
        if (err is not null)
            logger.LogError("PROD-006 RLS gate: {Error}", err);
        return err;
    }

    public string? ValidatePrivacyThreshold(bool smallCellSuppressed, bool reidentificationAttempted)
    {
        var err = ProdSemanticEvaluator.ValidatePrivacyThreshold(
            smallCellSuppressed, reidentificationAttempted, provider.Matrix.PrivacyThresholds);
        if (err is not null)
            logger.LogError("PROD-006 privacy gate: {Error}", err);
        return err;
    }

    public string? ValidateReportProvenance(bool hasVersion, bool hasAsOf, bool hasSource)
    {
        var err = ProdSemanticEvaluator.ValidateReportProvenance(
            hasVersion, hasAsOf, hasSource, provider.Matrix.Lineage);
        if (err is not null)
            logger.LogError("PROD-006 provenance gate: {Error}", err);
        return err;
    }

    public string? ValidateFreshness(bool withinSla)
    {
        var err = ProdSemanticEvaluator.ValidateFreshness(withinSla, provider.Matrix.Freshness);
        if (err is not null)
            logger.LogError("PROD-006 freshness gate: {Error}", err);
        return err;
    }

    public string? ValidateAuditCompleteness(IEnumerable<string> presentFields)
    {
        var err = ProdSemanticEvaluator.ValidateAuditCompleteness(presentFields, provider.Matrix.Audit);
        if (err is not null)
            logger.LogError("PROD-006 audit gate: {Error}", err);
        return err;
    }

    public string? ValidateSigning(ProdSemanticSigning? meta)
    {
        var err = ProdSemanticEvaluator.ValidateSigning(meta, provider.Matrix.SemanticLayerPolicyVersion);
        if (err is not null)
            logger.LogError("PROD-006 signing gate: {Error}", err);
        return err;
    }
}

public sealed class ProdSemanticStartupValidator(
    IProdSemanticProvider provider,
    ISemanticLayerClaimGuard claimGuard,
    ProdSemanticMetrics metrics,
    IHostEnvironment environment,
    ILogger<ProdSemanticStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        metrics.Recount();
        var opts = provider.Options;
        var failures = ProdSemanticEvaluator.Evaluate(provider.Matrix, opts.LiveSemanticLayerClaim);

        var claimErr = claimGuard.EnsureLiveSemanticAllowed();
        if (claimErr is not null && !failures.Contains(claimErr))
            failures.Add(claimErr);

        if (failures.Count > 0)
        {
            foreach (var f in failures)
                logger.LogError("PROD-006 semantic-layer rule: {Failure}", f);

            if (opts.EnforceOnStartup)
            {
                throw new InvalidOperationException(
                    "PROD-006 ProdSemantic startup failed: " + string.Join("; ", failures));
            }

            logger.LogWarning(
                "PROD-006: {Count} semantic-layer rule(s) failed but EnforceOnStartup=false",
                failures.Count);
        }

        logger.LogInformation(
            "PROD-006 prod semantic active enforce={Enforce} liveSemantic={Claim} auditFields={Fields} env={Env}",
            opts.EnforceOnStartup,
            opts.LiveSemanticLayerClaim,
            provider.Matrix.Audit?.RequiredFields.Count ?? 0,
            environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
