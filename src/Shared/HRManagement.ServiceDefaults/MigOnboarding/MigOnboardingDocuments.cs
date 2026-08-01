using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.MigOnboarding;

public sealed class MigOnboardingMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("onboardingPolicyVersion")]
    public string? OnboardingPolicyVersion { get; set; }

    [JsonPropertyName("readinessGates")]
    public List<MigReadinessGate> ReadinessGates { get; set; } = [];

    [JsonPropertyName("playbook")]
    public MigOnboardingPlaybook? Playbook { get; set; }

    [JsonPropertyName("sandboxCertification")]
    public MigSandboxCertification? SandboxCertification { get; set; }

    [JsonPropertyName("waveCriteria")]
    public MigWaveCriteria? WaveCriteria { get; set; }

    [JsonPropertyName("requiredOwners")]
    public List<string> RequiredOwners { get; set; } = [];

    [JsonPropertyName("telemetry")]
    public MigOnboardingTelemetry? Telemetry { get; set; }

    [JsonPropertyName("exitRollback")]
    public MigExitRollbackPolicy? ExitRollback { get; set; }

    [JsonPropertyName("agencies")]
    public List<MigOnboardingAgency> Agencies { get; set; } = [];

    [JsonPropertyName("signing")]
    public MigOnboardingSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<MigOnboardingBlocker> Blockers { get; set; } = [];
}

public sealed class MigReadinessGate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }
}

public sealed class MigOnboardingPlaybook
{
    [JsonPropertyName("noforkRequired")]
    public bool NoforkRequired { get; set; }

    [JsonPropertyName("str004Join")]
    public string? Str004Join { get; set; }

    [JsonPropertyName("standardSteps")]
    public List<string> StandardSteps { get; set; } = [];
}

public sealed class MigSandboxCertification
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("livePortalForbidden")]
    public bool LivePortalForbidden { get; set; }

    [JsonPropertyName("ext006Join")]
    public string? Ext006Join { get; set; }
}

public sealed class MigWaveCriteria
{
    [JsonPropertyName("signOffsRequiredBeforeWave")]
    public bool SignOffsRequiredBeforeWave { get; set; }

    [JsonPropertyName("requiredSignOffRoles")]
    public List<string> RequiredSignOffRoles { get; set; } = [];
}

public sealed class MigOnboardingTelemetry
{
    [JsonPropertyName("leadTimeTargetDays")]
    public int LeadTimeTargetDays { get; set; }

    [JsonPropertyName("defectBudget")]
    public int DefectBudget { get; set; }
}

public sealed class MigExitRollbackPolicy
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("modes")]
    public List<string> Modes { get; set; } = [];
}

public sealed class MigOnboardingAgency
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("policyVariant")]
    public string? PolicyVariant { get; set; }
}

public sealed class MigOnboardingSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("onboardingPolicyVersion")]
    public string? OnboardingPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class MigOnboardingBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
