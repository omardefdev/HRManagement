using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ProdSemantic;

public sealed class ProdSemanticMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("semanticLayerPolicyVersion")]
    public string? SemanticLayerPolicyVersion { get; set; }

    [JsonPropertyName("warehouseDecision")]
    public ProdSemanticWarehouseDecision? WarehouseDecision { get; set; }

    [JsonPropertyName("semanticMetrics")]
    public ProdSemanticMetricsSection? SemanticMetrics { get; set; }

    [JsonPropertyName("scdDimensions")]
    public ProdSemanticRequiredFlag? ScdDimensions { get; set; }

    [JsonPropertyName("tenantRegionRollups")]
    public ProdSemanticTenantRegionRollups? TenantRegionRollups { get; set; }

    [JsonPropertyName("statutoryReports")]
    public ProdSemanticRequiredFlag? StatutoryReports { get; set; }

    [JsonPropertyName("freshness")]
    public ProdSemanticRequiredFlag? Freshness { get; set; }

    [JsonPropertyName("lineage")]
    public ProdSemanticLineage? Lineage { get; set; }

    [JsonPropertyName("privacyThresholds")]
    public ProdSemanticPrivacyThresholds? PrivacyThresholds { get; set; }

    [JsonPropertyName("openDataAggregates")]
    public ProdSemanticOpenDataAggregates? OpenDataAggregates { get; set; }

    [JsonPropertyName("governedBi")]
    public ProdSemanticGovernedBi? GovernedBi { get; set; }

    [JsonPropertyName("audit")]
    public ProdSemanticAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ProdSemanticMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ProdSemanticSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ProdSemanticBlocker> Blockers { get; set; } = [];
}

public sealed class ProdSemanticRequiredFlag
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdSemanticWarehouseDecision
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("liveWarehouseForbidden")]
    public bool LiveWarehouseForbidden { get; set; }
}

public sealed class ProdSemanticMetricsSection
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("unifiedDefinitionRequired")]
    public bool UnifiedDefinitionRequired { get; set; }
}

public sealed class ProdSemanticTenantRegionRollups
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("rlsRequired")]
    public bool RlsRequired { get; set; }

    [JsonPropertyName("crossTenantForbidden")]
    public bool CrossTenantForbidden { get; set; }
}

public sealed class ProdSemanticLineage
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("versionAsOfSourceRequired")]
    public bool VersionAsOfSourceRequired { get; set; }
}

public sealed class ProdSemanticPrivacyThresholds
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("smallCellSuppressionRequired")]
    public bool SmallCellSuppressionRequired { get; set; }

    [JsonPropertyName("reidentificationForbidden")]
    public bool ReidentificationForbidden { get; set; }
}

public sealed class ProdSemanticOpenDataAggregates
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("liveFeedForbidden")]
    public bool LiveFeedForbidden { get; set; }
}

public sealed class ProdSemanticGovernedBi
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("liveDashboardForbidden")]
    public bool LiveDashboardForbidden { get; set; }
}

public sealed class ProdSemanticAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ProdSemanticMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveWarehouseForbidden")]
    public bool LiveWarehouseForbidden { get; set; }

    [JsonPropertyName("liveBiDashboardForbidden")]
    public bool LiveBiDashboardForbidden { get; set; }
}

public sealed class ProdSemanticSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("semanticLayerPolicyVersion")]
    public string? SemanticLayerPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ProdSemanticBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
