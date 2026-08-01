namespace HRManagement.ServiceDefaults.ProdSemantic;

public static class ProdSemanticEvaluator
{
    private static readonly string[] RequiredAuditFields =
        ["reportId", "kpiId", "tenantId", "asOf", "sourceRef", "outcome", "actor", "occurredAt", "evidenceRef"];

    public static List<string> Evaluate(ProdSemanticMatrixDocument matrix, bool liveSemanticLayerClaim)
    {
        var failures = new List<string>();

        if (!string.Equals(matrix.Card, "PROD-006", StringComparison.OrdinalIgnoreCase))
            failures.Add($"card must be PROD-006, got '{matrix.Card}'");

        failures.AddRange(EvaluateBlockers(matrix));
        failures.AddRange(EvaluateWarehouse(matrix.WarehouseDecision));
        failures.AddRange(EvaluateSemanticMetrics(matrix.SemanticMetrics));
        failures.AddRange(EvaluateRequired(matrix.ScdDimensions, "scdDimensions"));
        failures.AddRange(EvaluateTenantRollups(matrix.TenantRegionRollups));
        failures.AddRange(EvaluateRequired(matrix.StatutoryReports, "statutoryReports"));
        failures.AddRange(EvaluateRequired(matrix.Freshness, "freshness"));
        failures.AddRange(EvaluateLineage(matrix.Lineage));
        failures.AddRange(EvaluatePrivacy(matrix.PrivacyThresholds));
        failures.AddRange(EvaluateOpenData(matrix.OpenDataAggregates));
        failures.AddRange(EvaluateGovernedBi(matrix.GovernedBi));
        failures.AddRange(EvaluateAudit(matrix.Audit));
        failures.AddRange(EvaluateMockSandbox(matrix.MockSandbox));
        failures.AddRange(EvaluateSigning(matrix.Signing, matrix.SemanticLayerPolicyVersion));

        if (liveSemanticLayerClaim)
            failures.Add("LiveSemanticLayerClaim forbidden until PROD-006a-e live semantic workers");

        return failures;
    }

    public static List<string> EvaluateBlockers(ProdSemanticMatrixDocument matrix)
    {
        var failures = new List<string>();
        foreach (var need in new (string Id, string Status)[]
                 {
                     ("GOV-003", "Satisfied"),
                     ("FUN-006", "Satisfied"),
                     ("PERF-004", "Satisfied"),
                     ("TEN-003", "Satisfied"),
                     ("live-warehouse", "Blocked"),
                     ("live-bi-dashboard", "Blocked"),
                     ("live-open-data-feed", "Blocked"),
                     ("ANA-001", "NotStarted"),
                     ("REP-001", "NotStarted"),
                     ("PROD-007", "Satisfied")
                 })
        {
            var b = matrix.Blockers.FirstOrDefault(x =>
                string.Equals(x.Id, need.Id, StringComparison.OrdinalIgnoreCase));
            if (b is null)
                failures.Add($"blocker {need.Id} missing");
            else if (!string.Equals(b.Status, need.Status, StringComparison.OrdinalIgnoreCase))
                failures.Add($"blocker {need.Id} must be {need.Status} (got {b.Status})");
        }

        return failures;
    }

    public static List<string> EvaluateWarehouse(ProdSemanticWarehouseDecision? warehouse)
    {
        var failures = new List<string>();
        if (warehouse is null)
        {
            failures.Add("warehouseDecision missing");
            return failures;
        }

        if (!warehouse.Required)
            failures.Add("warehouseDecision.required must be true");
        if (!warehouse.LabStubAllowed)
            failures.Add("labStubAllowed must be true");
        if (!warehouse.LiveWarehouseForbidden)
            failures.Add("liveWarehouseForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSemanticMetrics(ProdSemanticMetricsSection? metrics)
    {
        var failures = new List<string>();
        if (metrics is null)
        {
            failures.Add("semanticMetrics missing");
            return failures;
        }

        if (!metrics.Required)
            failures.Add("semanticMetrics.required must be true");
        if (!metrics.UnifiedDefinitionRequired)
            failures.Add("unifiedDefinitionRequired must be true");
        return failures;
    }

    public static string? ValidateUnifiedKpiDefinition(bool definitionPresent, ProdSemanticMetricsSection? metrics)
    {
        if (!definitionPresent && (metrics?.UnifiedDefinitionRequired ?? true))
            return "KPI definition required";
        return null;
    }

    public static string? ValidateReconciliation(bool reconciledWithSource)
    {
        if (!reconciledWithSource)
            return "report must reconcile with source systems";
        return null;
    }

    public static List<string> EvaluateRequired(ProdSemanticRequiredFlag? section, string name)
    {
        if (section is null)
            return [$"{name} missing"];
        if (!section.Required)
            return [$"{name}.required must be true"];
        return [];
    }

    public static List<string> EvaluateTenantRollups(ProdSemanticTenantRegionRollups? rollups)
    {
        var failures = new List<string>();
        if (rollups is null)
        {
            failures.Add("tenantRegionRollups missing");
            return failures;
        }

        if (!rollups.Required)
            failures.Add("tenantRegionRollups.required must be true");
        if (!rollups.RlsRequired)
            failures.Add("rlsRequired must be true");
        if (!rollups.CrossTenantForbidden)
            failures.Add("crossTenantForbidden must be true");
        return failures;
    }

    public static string? ValidateTenantRls(bool withinTenant, ProdSemanticTenantRegionRollups? rollups)
    {
        if (!withinTenant && (rollups?.CrossTenantForbidden ?? true))
            return "cross-tenant rollup forbidden";
        return null;
    }

    public static List<string> EvaluateLineage(ProdSemanticLineage? lineage)
    {
        var failures = new List<string>();
        if (lineage is null)
        {
            failures.Add("lineage missing");
            return failures;
        }

        if (!lineage.Required)
            failures.Add("lineage.required must be true");
        if (!lineage.VersionAsOfSourceRequired)
            failures.Add("versionAsOfSourceRequired must be true");
        return failures;
    }

    public static string? ValidateReportProvenance(
        bool hasVersion,
        bool hasAsOf,
        bool hasSource,
        ProdSemanticLineage? lineage)
    {
        if ((!hasVersion || !hasAsOf || !hasSource) && (lineage?.VersionAsOfSourceRequired ?? true))
            return "report provenance requires version/as-of/source";
        return null;
    }

    public static string? ValidateFreshness(bool withinSla, ProdSemanticRequiredFlag? freshness)
    {
        if (!withinSla && (freshness?.Required ?? true))
            return "freshness SLA required";
        return null;
    }

    public static List<string> EvaluatePrivacy(ProdSemanticPrivacyThresholds? privacy)
    {
        var failures = new List<string>();
        if (privacy is null)
        {
            failures.Add("privacyThresholds missing");
            return failures;
        }

        if (!privacy.Required)
            failures.Add("privacyThresholds.required must be true");
        if (!privacy.SmallCellSuppressionRequired)
            failures.Add("smallCellSuppressionRequired must be true");
        if (!privacy.ReidentificationForbidden)
            failures.Add("reidentificationForbidden must be true");
        return failures;
    }

    public static string? ValidatePrivacyThreshold(
        bool smallCellSuppressed,
        bool reidentificationAttempted,
        ProdSemanticPrivacyThresholds? privacy)
    {
        if (!smallCellSuppressed && (privacy?.SmallCellSuppressionRequired ?? true))
            return "small-cell suppression required";
        if (reidentificationAttempted && (privacy?.ReidentificationForbidden ?? true))
            return "reidentification attempt forbidden";
        return null;
    }

    public static List<string> EvaluateOpenData(ProdSemanticOpenDataAggregates? openData)
    {
        var failures = new List<string>();
        if (openData is null)
        {
            failures.Add("openDataAggregates missing");
            return failures;
        }

        if (!openData.Required)
            failures.Add("openDataAggregates.required must be true");
        if (!openData.LabStubAllowed)
            failures.Add("openDataAggregates.labStubAllowed must be true");
        if (!openData.LiveFeedForbidden)
            failures.Add("liveFeedForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateGovernedBi(ProdSemanticGovernedBi? bi)
    {
        var failures = new List<string>();
        if (bi is null)
        {
            failures.Add("governedBi missing");
            return failures;
        }

        if (!bi.Required)
            failures.Add("governedBi.required must be true");
        if (!bi.LabStubAllowed)
            failures.Add("governedBi.labStubAllowed must be true");
        if (!bi.LiveDashboardForbidden)
            failures.Add("liveDashboardForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateAudit(ProdSemanticAudit? audit)
    {
        var failures = new List<string>();
        if (audit is null)
        {
            failures.Add("audit missing");
            return failures;
        }

        if (!audit.Required)
            failures.Add("audit.required must be true");
        foreach (var need in RequiredAuditFields)
        {
            if (!audit.RequiredFields.Any(f => string.Equals(f, need, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"audit missing {need}");
        }

        return failures;
    }

    public static string? ValidateAuditCompleteness(IEnumerable<string> presentFields, ProdSemanticAudit? audit)
    {
        var required = audit?.RequiredFields ?? RequiredAuditFields.ToList();
        var present = presentFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var need in required)
        {
            if (!present.Contains(need))
                return $"audit missing field {need}";
        }

        return null;
    }

    public static List<string> EvaluateMockSandbox(ProdSemanticMockSandbox? sandbox)
    {
        var failures = new List<string>();
        if (sandbox is null)
        {
            failures.Add("mockSandbox missing");
            return failures;
        }

        if (!sandbox.LabStub)
            failures.Add("mockSandbox.labStub must be true");
        if (!sandbox.LiveWarehouseForbidden)
            failures.Add("liveWarehouseForbidden must be true");
        if (!sandbox.LiveBiDashboardForbidden)
            failures.Add("liveBiDashboardForbidden must be true");
        return failures;
    }

    public static List<string> EvaluateSigning(ProdSemanticSigning? sig, string? policyVersion)
    {
        var err = ValidateSigning(sig, policyVersion);
        return err is null ? [] : [err];
    }

    public static string? ValidateSigning(ProdSemanticSigning? sig, string? policyVersion = null)
    {
        if (sig is null)
            return "signing missing";
        if (string.IsNullOrWhiteSpace(sig.SignedByRole) || string.IsNullOrWhiteSpace(sig.ReviewerRole))
            return "signing missing roles";
        if (string.Equals(sig.SignedByRole, sig.ReviewerRole, StringComparison.OrdinalIgnoreCase))
            return "signing owner equals reviewer";
        var version = sig.SemanticLayerPolicyVersion ?? policyVersion;
        if (string.IsNullOrWhiteSpace(sig.ContentHash) || string.IsNullOrWhiteSpace(version))
            return "signing missing hash/version";
        return null;
    }
}
