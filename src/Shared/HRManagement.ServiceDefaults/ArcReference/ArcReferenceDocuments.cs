using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ArcReference;

public sealed class ArcReferenceMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("referenceArchitecturePolicyVersion")]
    public string? ReferenceArchitecturePolicyVersion { get; set; }

    [JsonPropertyName("boundedContexts")]
    public ArcReferenceBoundedContexts? BoundedContexts { get; set; }

    [JsonPropertyName("syncVsEvent")]
    public ArcReferenceSyncVsEvent? SyncVsEvent { get; set; }

    [JsonPropertyName("dataOwnership")]
    public ArcReferenceDataOwnership? DataOwnership { get; set; }

    [JsonPropertyName("multiSiteProfiles")]
    public ArcReferenceMultiSiteProfiles? MultiSiteProfiles { get; set; }

    [JsonPropertyName("tenancyBoundaries")]
    public ArcReferenceTenancyBoundaries? TenancyBoundaries { get; set; }

    [JsonPropertyName("controlDataPlanes")]
    public ArcReferenceControlDataPlanes? ControlDataPlanes { get; set; }

    [JsonPropertyName("supportedTopologies")]
    public ArcReferenceSupportedTopologies? SupportedTopologies { get; set; }

    [JsonPropertyName("couplingGuardrails")]
    public ArcReferenceCouplingGuardrails? CouplingGuardrails { get; set; }

    [JsonPropertyName("fitnessEnvelope")]
    public ArcReferenceFitnessEnvelope? FitnessEnvelope { get; set; }

    [JsonPropertyName("audit")]
    public ArcReferenceAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ArcReferenceMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ArcReferenceSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ArcReferenceBlocker> Blockers { get; set; } = [];

    [JsonPropertyName("contexts")]
    public List<ArcReferenceContext> Contexts { get; set; } = [];
}

public sealed class ArcReferenceBoundedContexts
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("everyContextHasOwnerAndRationaleRequired")]
    public bool EveryContextHasOwnerAndRationaleRequired { get; set; }

    [JsonPropertyName("noUnjustifiedDistributedComplexityRequired")]
    public bool NoUnjustifiedDistributedComplexityRequired { get; set; }
}

public sealed class ArcReferenceSyncVsEvent
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("interactionStyleDocumentedRequired")]
    public bool InteractionStyleDocumentedRequired { get; set; }
}

public sealed class ArcReferenceDataOwnership
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("singleWriterPerAggregateRequired")]
    public bool SingleWriterPerAggregateRequired { get; set; }
}

public sealed class ArcReferenceMultiSiteProfiles
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("sameArtifactPrimaryAndDrRequired")]
    public bool SameArtifactPrimaryAndDrRequired { get; set; }

    [JsonPropertyName("primaryAndDrProfilesRequired")]
    public bool PrimaryAndDrProfilesRequired { get; set; }
}

public sealed class ArcReferenceTenancyBoundaries
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("tenantModelAlignedRequired")]
    public bool TenantModelAlignedRequired { get; set; }
}

public sealed class ArcReferenceControlDataPlanes
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("planesSeparatedRequired")]
    public bool PlanesSeparatedRequired { get; set; }
}

public sealed class ArcReferenceSupportedTopologies
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("unsupportedTopologyRejectedRequired")]
    public bool UnsupportedTopologyRejectedRequired { get; set; }

    [JsonPropertyName("envelopeValidatedRequired")]
    public bool EnvelopeValidatedRequired { get; set; }
}

public sealed class ArcReferenceCouplingGuardrails
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("noDirectDbCrossBoundaryRequired")]
    public bool NoDirectDbCrossBoundaryRequired { get; set; }

    [JsonPropertyName("noDomainCouplingAcrossContextsRequired")]
    public bool NoDomainCouplingAcrossContextsRequired { get; set; }
}

public sealed class ArcReferenceFitnessEnvelope
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("referenceImplementationRequired")]
    public bool ReferenceImplementationRequired { get; set; }

    [JsonPropertyName("labFitnessHarnessRequired")]
    public bool LabFitnessHarnessRequired { get; set; }
}

public sealed class ArcReferenceAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ArcReferenceMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveTwoSiteDeploymentForbidden")]
    public bool LiveTwoSiteDeploymentForbidden { get; set; }

    [JsonPropertyName("liveArchitectureFitnessEnvelopeForbidden")]
    public bool LiveArchitectureFitnessEnvelopeForbidden { get; set; }

    [JsonPropertyName("liveCouplingBoundaryScanForbidden")]
    public bool LiveCouplingBoundaryScanForbidden { get; set; }

    [JsonPropertyName("liveReferenceImplementationSignoffForbidden")]
    public bool LiveReferenceImplementationSignoffForbidden { get; set; }
}

public sealed class ArcReferenceSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("referenceArchitecturePolicyVersion")]
    public string? ReferenceArchitecturePolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ArcReferenceBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed class ArcReferenceContext
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("ownerRole")]
    public string? OwnerRole { get; set; }

    [JsonPropertyName("stayOrMerge")]
    public string? StayOrMerge { get; set; }

    [JsonPropertyName("interactionStyle")]
    public string? InteractionStyle { get; set; }

    [JsonPropertyName("rationale")]
    public string? Rationale { get; set; }
}
