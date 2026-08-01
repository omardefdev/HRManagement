using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.NoFork;

public sealed class NoForkMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("releaseDigest")]
    public string ReleaseDigest { get; set; } = "";

    [JsonPropertyName("forbiddenCodePatterns")]
    public List<string> ForbiddenCodePatterns { get; set; } = [];

    [JsonPropertyName("blockers")]
    public List<NoForkBlocker> Blockers { get; set; } = [];

    [JsonPropertyName("agencyFixtures")]
    public AgencyFixturesRef? AgencyFixtures { get; set; }

    [JsonPropertyName("signedConfigPackSchema")]
    public SignedConfigPackSchema? SignedConfigPackSchema { get; set; }
}

public sealed class NoForkBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed class AgencyFixturesRef
{
    [JsonPropertyName("agencyAPath")]
    public string? AgencyAPath { get; set; }

    [JsonPropertyName("agencyBPath")]
    public string? AgencyBPath { get; set; }

    [JsonPropertyName("requireSameReleaseDigest")]
    public bool RequireSameReleaseDigest { get; set; } = true;
}

public sealed class SignedConfigPackSchema
{
    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class AgencyConfigPack
{
    [JsonPropertyName("packId")]
    public string PackId { get; set; } = "";

    [JsonPropertyName("agencyId")]
    public string AgencyId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("releaseDigest")]
    public string ReleaseDigest { get; set; } = "";

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = "";

    [JsonPropertyName("rollbackRef")]
    public string? RollbackRef { get; set; }

    [JsonPropertyName("reviewerRoles")]
    public List<string> ReviewerRoles { get; set; } = [];

    [JsonPropertyName("branding")]
    public AgencyBrandingPayload? Branding { get; set; }

    [JsonPropertyName("enabledOptionalModules")]
    public List<string> EnabledOptionalModules { get; set; } = [];

    [JsonPropertyName("disabledOptionalModules")]
    public List<string> DisabledOptionalModules { get; set; } = [];

    [JsonPropertyName("policyNotes")]
    public string? PolicyNotes { get; set; }
}

public sealed class AgencyBrandingPayload
{
    [JsonPropertyName("organizationName")]
    public string? OrganizationName { get; set; }

    [JsonPropertyName("primaryColor")]
    public string? PrimaryColor { get; set; }

    [JsonPropertyName("shortName")]
    public string? ShortName { get; set; }

    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; set; }
}
