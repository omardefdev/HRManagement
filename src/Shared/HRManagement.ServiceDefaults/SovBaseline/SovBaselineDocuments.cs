using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.SovBaseline;

public sealed class SovBaselineMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("controlBaselinePolicyVersion")]
    public string? ControlBaselinePolicyVersion { get; set; }

    [JsonPropertyName("frameworks")]
    public SovBaselineFrameworks? Frameworks { get; set; }

    [JsonPropertyName("impactCategorization")]
    public SovBaselineRequiredFlag? ImpactCategorization { get; set; }

    [JsonPropertyName("controlCoverage")]
    public SovBaselineControlCoverage? ControlCoverage { get; set; }

    [JsonPropertyName("ownershipEvidence")]
    public SovBaselineOwnershipEvidence? OwnershipEvidence { get; set; }

    [JsonPropertyName("poam")]
    public SovBaselinePoam? Poam { get; set; }

    [JsonPropertyName("exceptions")]
    public SovBaselineExceptions? Exceptions { get; set; }

    [JsonPropertyName("authorizationBoundary")]
    public SovBaselineRequiredFlag? AuthorizationBoundary { get; set; }

    [JsonPropertyName("reassessment")]
    public SovBaselineReassessment? Reassessment { get; set; }

    [JsonPropertyName("independentAssessment")]
    public SovBaselineIndependentAssessment? IndependentAssessment { get; set; }

    [JsonPropertyName("audit")]
    public SovBaselineAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public SovBaselineMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public SovBaselineSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<SovBaselineBlocker> Blockers { get; set; } = [];
}

public sealed class SovBaselineRequiredFlag
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class SovBaselineFrameworks
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("localLawTailoringRequired")]
    public bool LocalLawTailoringRequired { get; set; }

    [JsonPropertyName("crosswalk")]
    public List<string> Crosswalk { get; set; } = [];
}

public sealed class SovBaselineControlCoverage
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("fullCoverageRequired")]
    public bool FullCoverageRequired { get; set; }

    [JsonPropertyName("allowedStatuses")]
    public List<string> AllowedStatuses { get; set; } = [];

    [JsonPropertyName("siteReasonRequiredForNa")]
    public bool SiteReasonRequiredForNa { get; set; }
}

public sealed class SovBaselineOwnershipEvidence
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("ownerRequired")]
    public bool OwnerRequired { get; set; }

    [JsonPropertyName("evidenceRequired")]
    public bool EvidenceRequired { get; set; }

    [JsonPropertyName("testRefRequired")]
    public bool TestRefRequired { get; set; }

    [JsonPropertyName("frequencyRequired")]
    public bool FrequencyRequired { get; set; }
}

public sealed class SovBaselinePoam
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("datedRemediationRequired")]
    public bool DatedRemediationRequired { get; set; }
}

public sealed class SovBaselineExceptions
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("signedExceptionRequired")]
    public bool SignedExceptionRequired { get; set; }
}

public sealed class SovBaselineReassessment
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("afterMaterialChangeRequired")]
    public bool AfterMaterialChangeRequired { get; set; }
}

public sealed class SovBaselineIndependentAssessment
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("liveAtoForbidden")]
    public bool LiveAtoForbidden { get; set; }
}

public sealed class SovBaselineAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class SovBaselineMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveAtoForbidden")]
    public bool LiveAtoForbidden { get; set; }

    [JsonPropertyName("livePoamSystemForbidden")]
    public bool LivePoamSystemForbidden { get; set; }

    [JsonPropertyName("liveContinuousAssessmentForbidden")]
    public bool LiveContinuousAssessmentForbidden { get; set; }
}

public sealed class SovBaselineSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("controlBaselinePolicyVersion")]
    public string? ControlBaselinePolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class SovBaselineBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
