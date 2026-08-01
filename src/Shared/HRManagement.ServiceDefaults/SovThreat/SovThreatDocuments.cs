using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.SovThreat;

public sealed class SovThreatMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("threatModelPolicyVersion")]
    public string? ThreatModelPolicyVersion { get; set; }

    [JsonPropertyName("methodology")]
    public SovThreatMethodology? Methodology { get; set; }

    [JsonPropertyName("trustBoundaries")]
    public SovThreatTrustBoundaries? TrustBoundaries { get; set; }

    [JsonPropertyName("assetsActors")]
    public SovThreatRequiredFlag? AssetsActors { get; set; }

    [JsonPropertyName("threatCategories")]
    public SovThreatCategories? ThreatCategories { get; set; }

    [JsonPropertyName("highThreatCoverage")]
    public SovThreatHighCoverage? HighThreatCoverage { get; set; }

    [JsonPropertyName("changeTrigger")]
    public SovThreatChangeTrigger? ChangeTrigger { get; set; }

    [JsonPropertyName("misuseStories")]
    public SovThreatMisuseStories? MisuseStories { get; set; }

    [JsonPropertyName("residualRisk")]
    public SovThreatResidualRisk? ResidualRisk { get; set; }

    [JsonPropertyName("audit")]
    public SovThreatAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public SovThreatMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public SovThreatSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<SovThreatBlocker> Blockers { get; set; } = [];
}

public sealed class SovThreatRequiredFlag
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class SovThreatMethodology
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("strideRequired")]
    public bool StrideRequired { get; set; }
}

public sealed class SovThreatTrustBoundaries
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("inventoryRequired")]
    public bool InventoryRequired { get; set; }
}

public sealed class SovThreatCategories
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];
}

public sealed class SovThreatHighCoverage
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("controlRequired")]
    public bool ControlRequired { get; set; }

    [JsonPropertyName("testRequired")]
    public bool TestRequired { get; set; }

    [JsonPropertyName("ownerRequired")]
    public bool OwnerRequired { get; set; }
}

public sealed class SovThreatChangeTrigger
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("materialChangeUpdateRequired")]
    public bool MaterialChangeUpdateRequired { get; set; }
}

public sealed class SovThreatMisuseStories
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("backlogLinkageRequired")]
    public bool BacklogLinkageRequired { get; set; }
}

public sealed class SovThreatResidualRisk
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("signedRequired")]
    public bool SignedRequired { get; set; }
}

public sealed class SovThreatAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class SovThreatMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveIndependentTmReviewForbidden")]
    public bool LiveIndependentTmReviewForbidden { get; set; }

    [JsonPropertyName("liveAttackTreeWalkthroughForbidden")]
    public bool LiveAttackTreeWalkthroughForbidden { get; set; }

    [JsonPropertyName("liveMisuseBacklogSyncForbidden")]
    public bool LiveMisuseBacklogSyncForbidden { get; set; }

    [JsonPropertyName("liveResidualRiskSignoffForbidden")]
    public bool LiveResidualRiskSignoffForbidden { get; set; }
}

public sealed class SovThreatSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("threatModelPolicyVersion")]
    public string? ThreatModelPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class SovThreatBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
