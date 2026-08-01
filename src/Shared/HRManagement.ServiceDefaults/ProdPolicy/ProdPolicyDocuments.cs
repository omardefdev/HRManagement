using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ProdPolicy;

public sealed class ProdPolicyMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("policyPlatformPolicyVersion")]
    public string? PolicyPlatformPolicyVersion { get; set; }

    [JsonPropertyName("versionedTemplates")]
    public ProdPolicyVersionedTemplates? VersionedTemplates { get; set; }

    [JsonPropertyName("effectiveDating")]
    public ProdPolicyEffectiveDating? EffectiveDating { get; set; }

    [JsonPropertyName("fourEyesPublish")]
    public ProdPolicyFourEyesPublish? FourEyesPublish { get; set; }

    [JsonPropertyName("simulation")]
    public ProdPolicySimulation? Simulation { get; set; }

    [JsonPropertyName("rollback")]
    public ProdPolicyRollback? Rollback { get; set; }

    [JsonPropertyName("tenantOverride")]
    public ProdPolicyTenantOverride? TenantOverride { get; set; }

    [JsonPropertyName("noArbitraryCode")]
    public bool NoArbitraryCode { get; set; }

    [JsonPropertyName("invalidPublishForbidden")]
    public bool InvalidPublishForbidden { get; set; }

    [JsonPropertyName("changeWithoutDeployOrFork")]
    public bool ChangeWithoutDeployOrFork { get; set; }

    [JsonPropertyName("audit")]
    public ProdPolicyAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ProdPolicyMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ProdPolicySigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ProdPolicyBlocker> Blockers { get; set; } = [];
}

public sealed class ProdPolicyVersionedTemplates
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("kinds")]
    public List<string> Kinds { get; set; } = [];
}

public sealed class ProdPolicyEffectiveDating
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("historicalVersionPinned")]
    public bool HistoricalVersionPinned { get; set; }
}

public sealed class ProdPolicyFourEyesPublish
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("publisherRoleEqualsReviewerForbidden")]
    public bool PublisherRoleEqualsReviewerForbidden { get; set; }
}

public sealed class ProdPolicySimulation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("syntheticOnly")]
    public bool SyntheticOnly { get; set; }

    [JsonPropertyName("realDataForbidden")]
    public bool RealDataForbidden { get; set; }
}

public sealed class ProdPolicyRollback
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdPolicyTenantOverride
{
    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    [JsonPropertyName("cannotBypassNationalMandatory")]
    public bool CannotBypassNationalMandatory { get; set; }
}

public sealed class ProdPolicyAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ProdPolicyMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveEngineForbidden")]
    public bool LiveEngineForbidden { get; set; }

    [JsonPropertyName("livePublishForbidden")]
    public bool LivePublishForbidden { get; set; }
}

public sealed class ProdPolicySigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("policyPlatformPolicyVersion")]
    public string? PolicyPlatformPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ProdPolicyBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
