using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ExtSandbox;

public sealed class ExtSandboxMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("sandboxCertPolicyVersion")]
    public string? SandboxCertPolicyVersion { get; set; }

    [JsonPropertyName("syntheticDataOnly")]
    public bool SyntheticDataOnly { get; set; }

    [JsonPropertyName("realPiiForbidden")]
    public bool RealPiiForbidden { get; set; }

    [JsonPropertyName("credentials")]
    public ExtSandboxCredentials? Credentials { get; set; }

    [JsonPropertyName("conformance")]
    public ExtSandboxConformance? Conformance { get; set; }

    [JsonPropertyName("certification")]
    public ExtSandboxCertification? Certification { get; set; }

    [JsonPropertyName("docsMatchPublishedContract")]
    public bool DocsMatchPublishedContract { get; set; }

    [JsonPropertyName("rateErrorSimulation")]
    public ExtSandboxRateErrorSimulation? RateErrorSimulation { get; set; }

    [JsonPropertyName("supportAnalytics")]
    public ExtSandboxSupportAnalytics? SupportAnalytics { get; set; }

    [JsonPropertyName("audit")]
    public ExtSandboxAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ExtSandboxMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ExtSandboxSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ExtSandboxBlocker> Blockers { get; set; } = [];
}

public sealed class ExtSandboxCredentials
{
    [JsonPropertyName("ephemeralRequired")]
    public bool EphemeralRequired { get; set; }

    [JsonPropertyName("manualSecretsForbidden")]
    public bool ManualSecretsForbidden { get; set; }

    [JsonPropertyName("noProdAccess")]
    public bool NoProdAccess { get; set; }
}

public sealed class ExtSandboxConformance
{
    [JsonPropertyName("negativeCasesRequired")]
    public bool NegativeCasesRequired { get; set; }

    [JsonPropertyName("incompatibleClientMustFail")]
    public bool IncompatibleClientMustFail { get; set; }
}

public sealed class ExtSandboxCertification
{
    [JsonPropertyName("requiredBeforeProdJoin")]
    public bool RequiredBeforeProdJoin { get; set; }

    [JsonPropertyName("revocationSupported")]
    public bool RevocationSupported { get; set; }
}

public sealed class ExtSandboxRateErrorSimulation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ExtSandboxSupportAnalytics
{
    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("liveForbidden")]
    public bool LiveForbidden { get; set; }
}

public sealed class ExtSandboxAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ExtSandboxMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("livePortalForbidden")]
    public bool LivePortalForbidden { get; set; }

    [JsonPropertyName("realDataForbidden")]
    public bool RealDataForbidden { get; set; }
}

public sealed class ExtSandboxSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("sandboxCertPolicyVersion")]
    public string? SandboxCertPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ExtSandboxBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
