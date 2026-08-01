using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ExtCivil;

public sealed class ExtCivilMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("civilRegistryPolicyVersion")]
    public string? CivilRegistryPolicyVersion { get; set; }

    [JsonPropertyName("purposeAllowlist")]
    public List<string> PurposeAllowlist { get; set; } = [];

    [JsonPropertyName("consentRequired")]
    public bool ConsentRequired { get; set; }

    [JsonPropertyName("minimalQuery")]
    public ExtCivilMinimalQuery? MinimalQuery { get; set; }

    [JsonPropertyName("matchOutcomes")]
    public List<string> MatchOutcomes { get; set; } = [];

    [JsonPropertyName("mismatchHumanWorkflow")]
    public ExtCivilMismatchWorkflow? MismatchHumanWorkflow { get; set; }

    [JsonPropertyName("caching")]
    public ExtCivilCaching? Caching { get; set; }

    [JsonPropertyName("corrections")]
    public ExtCivilCorrections? Corrections { get; set; }

    [JsonPropertyName("unavailableMode")]
    public ExtCivilUnavailableMode? UnavailableMode { get; set; }

    [JsonPropertyName("rateLimits")]
    public ExtCivilRateLimits? RateLimits { get; set; }

    [JsonPropertyName("audit")]
    public ExtCivilAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ExtCivilMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ExtCivilSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ExtCivilBlocker> Blockers { get; set; } = [];
}

public sealed class ExtCivilMinimalQuery
{
    [JsonPropertyName("allowedRequestFields")]
    public List<string> AllowedRequestFields { get; set; } = [];

    [JsonPropertyName("allowedResponseFields")]
    public List<string> AllowedResponseFields { get; set; } = [];
}

public sealed class ExtCivilMismatchWorkflow
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("queue")]
    public string? Queue { get; set; }
}

public sealed class ExtCivilCaching
{
    [JsonPropertyName("ttlSeconds")]
    public int TtlSeconds { get; set; }

    [JsonPropertyName("forbidStaleAsTruth")]
    public bool ForbidStaleAsTruth { get; set; }
}

public sealed class ExtCivilCorrections
{
    [JsonPropertyName("workflowRequired")]
    public bool WorkflowRequired { get; set; }
}

public sealed class ExtCivilUnavailableMode
{
    [JsonPropertyName("degradeBehavior")]
    public string? DegradeBehavior { get; set; }

    [JsonPropertyName("forbidUnverifiedAsTruth")]
    public bool ForbidUnverifiedAsTruth { get; set; }
}

public sealed class ExtCivilRateLimits
{
    [JsonPropertyName("maxQueriesPerMinute")]
    public int MaxQueriesPerMinute { get; set; }

    [JsonPropertyName("bulkHarvestForbidden")]
    public bool BulkHarvestForbidden { get; set; }
}

public sealed class ExtCivilAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ExtCivilMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveMoiForbidden")]
    public bool LiveMoiForbidden { get; set; }

    [JsonPropertyName("portJoin")]
    public string? PortJoin { get; set; }
}

public sealed class ExtCivilSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("civilRegistryPolicyVersion")]
    public string? CivilRegistryPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ExtCivilBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
