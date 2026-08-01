using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ProdAgency;

public sealed class ProdAgencyMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("agencySelfAdminPolicyVersion")]
    public string? AgencySelfAdminPolicyVersion { get; set; }

    [JsonPropertyName("delegatedAdmin")]
    public ProdAgencyDelegatedAdmin? DelegatedAdmin { get; set; }

    [JsonPropertyName("nationalControlsImmutable")]
    public bool NationalControlsImmutable { get; set; }

    [JsonPropertyName("crossTenantAdminForbidden")]
    public bool CrossTenantAdminForbidden { get; set; }

    [JsonPropertyName("rollback")]
    public ProdAgencyRollback? Rollback { get; set; }

    [JsonPropertyName("audit")]
    public ProdAgencyAudit? Audit { get; set; }

    [JsonPropertyName("fourEyesSensitive")]
    public ProdAgencyFourEyesSensitive? FourEyesSensitive { get; set; }

    [JsonPropertyName("preview")]
    public ProdAgencyPreview? Preview { get; set; }

    [JsonPropertyName("separationNationalAgency")]
    public ProdAgencySeparation? SeparationNationalAgency { get; set; }

    [JsonPropertyName("supportEscalation")]
    public ProdAgencySupportEscalation? SupportEscalation { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ProdAgencyMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ProdAgencySigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ProdAgencyBlocker> Blockers { get; set; } = [];
}

public sealed class ProdAgencyDelegatedAdmin
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("noDbaDeveloperRequired")]
    public bool NoDbaDeveloperRequired { get; set; }

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = [];
}

public sealed class ProdAgencyRollback
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdAgencyAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ProdAgencyFourEyesSensitive
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("publisherRoleEqualsReviewerForbidden")]
    public bool PublisherRoleEqualsReviewerForbidden { get; set; }
}

public sealed class ProdAgencyPreview
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdAgencySeparation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdAgencySupportEscalation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdAgencyMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveSelfAdminForbidden")]
    public bool LiveSelfAdminForbidden { get; set; }

    [JsonPropertyName("liveDelegatedUiForbidden")]
    public bool LiveDelegatedUiForbidden { get; set; }
}

public sealed class ProdAgencySigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("agencySelfAdminPolicyVersion")]
    public string? AgencySelfAdminPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ProdAgencyBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
