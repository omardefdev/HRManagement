using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ArcCriticality;

public sealed class ArcCriticalityMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("criticalityPolicyVersion")]
    public string? CriticalityPolicyVersion { get; set; }

    [JsonPropertyName("tierClassification")]
    public ArcCriticalityTiers? TierClassification { get; set; }

    [JsonPropertyName("journeyDependencyMap")]
    public ArcCriticalityJourneyMap? JourneyDependencyMap { get; set; }

    [JsonPropertyName("sharedPlatformDeps")]
    public ArcCriticalitySharedPlatform? SharedPlatformDeps { get; set; }

    [JsonPropertyName("failureDomains")]
    public ArcCriticalityFailureDomains? FailureDomains { get; set; }

    [JsonPropertyName("numericTargetsTelemetry")]
    public ArcCriticalityNumericTargets? NumericTargetsTelemetry { get; set; }

    [JsonPropertyName("spofPolicy")]
    public ArcCriticalitySpofPolicy? SpofPolicy { get; set; }

    [JsonPropertyName("recoveryOrder")]
    public ArcCriticalityRecoveryOrder? RecoveryOrder { get; set; }

    [JsonPropertyName("gracefulDegradation")]
    public ArcCriticalityGracefulDegradation? GracefulDegradation { get; set; }

    [JsonPropertyName("audit")]
    public ArcCriticalityAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ArcCriticalityMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ArcCriticalitySigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ArcCriticalityBlocker> Blockers { get; set; } = [];

    [JsonPropertyName("components")]
    public List<ArcCriticalityComponent> Components { get; set; } = [];
}

public sealed class ArcCriticalityTiers
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("allServicesClassifiedRequired")]
    public bool AllServicesClassifiedRequired { get; set; }

    [JsonPropertyName("allDependenciesClassifiedRequired")]
    public bool AllDependenciesClassifiedRequired { get; set; }

    [JsonPropertyName("tiers")]
    public List<string> Tiers { get; set; } = [];
}

public sealed class ArcCriticalityJourneyMap
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("criticalJourneysMappedRequired")]
    public bool CriticalJourneysMappedRequired { get; set; }
}

public sealed class ArcCriticalitySharedPlatform
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("surfaces")]
    public List<string> Surfaces { get; set; } = [];
}

public sealed class ArcCriticalityFailureDomains
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("domainsDocumentedRequired")]
    public bool DomainsDocumentedRequired { get; set; }
}

public sealed class ArcCriticalityNumericTargets
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("everyNumericTargetHasTelemetryRequired")]
    public bool EveryNumericTargetHasTelemetryRequired { get; set; }
}

public sealed class ArcCriticalitySpofPolicy
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("noUnacceptableSpofInTier01Required")]
    public bool NoUnacceptableSpofInTier01Required { get; set; }
}

public sealed class ArcCriticalityRecoveryOrder
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("orderedStepsRequired")]
    public bool OrderedStepsRequired { get; set; }

    [JsonPropertyName("drillEvidenceRequired")]
    public bool DrillEvidenceRequired { get; set; }
}

public sealed class ArcCriticalityGracefulDegradation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("tier01DegradationDocumentedRequired")]
    public bool Tier01DegradationDocumentedRequired { get; set; }
}

public sealed class ArcCriticalityAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ArcCriticalityMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveArchitectureFailureWalkthroughForbidden")]
    public bool LiveArchitectureFailureWalkthroughForbidden { get; set; }

    [JsonPropertyName("liveControlledDependencyShutdownForbidden")]
    public bool LiveControlledDependencyShutdownForbidden { get; set; }

    [JsonPropertyName("liveRecoveryOrderDrillForbidden")]
    public bool LiveRecoveryOrderDrillForbidden { get; set; }

    [JsonPropertyName("liveFullDependencyDiscoveryForbidden")]
    public bool LiveFullDependencyDiscoveryForbidden { get; set; }
}

public sealed class ArcCriticalitySigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("criticalityPolicyVersion")]
    public string? CriticalityPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ArcCriticalityBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed class ArcCriticalityComponent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "";

    [JsonPropertyName("failureDomain")]
    public string? FailureDomain { get; set; }

    [JsonPropertyName("recoveryStep")]
    public string? RecoveryStep { get; set; }

    [JsonPropertyName("telemetryRef")]
    public string? TelemetryRef { get; set; }
}
