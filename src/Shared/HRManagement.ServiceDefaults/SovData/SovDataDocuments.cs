using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.SovData;

public sealed class SovDataMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("dataSovereigntyPolicyVersion")]
    public string? DataSovereigntyPolicyVersion { get; set; }

    [JsonPropertyName("dataCopyInventory")]
    public SovDataCopyInventory? DataCopyInventory { get; set; }

    [JsonPropertyName("dataFlowMap")]
    public SovDataRequiredFlag? DataFlowMap { get; set; }

    [JsonPropertyName("residency")]
    public SovDataResidency? Residency { get; set; }

    [JsonPropertyName("egress")]
    public SovDataEgress? Egress { get; set; }

    [JsonPropertyName("remoteSupport")]
    public SovDataRemoteSupport? RemoteSupport { get; set; }

    [JsonPropertyName("subcontractors")]
    public SovDataSubcontractors? Subcontractors { get; set; }

    [JsonPropertyName("telemetry")]
    public SovDataTelemetry? Telemetry { get; set; }

    [JsonPropertyName("destruction")]
    public SovDataDestruction? Destruction { get; set; }

    [JsonPropertyName("lawfulDisclosure")]
    public SovDataLawfulDisclosure? LawfulDisclosure { get; set; }

    [JsonPropertyName("audit")]
    public SovDataAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public SovDataMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public SovDataSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<SovDataBlocker> Blockers { get; set; } = [];
}

public sealed class SovDataRequiredFlag
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class SovDataCopyInventory
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("classificationRequired")]
    public bool ClassificationRequired { get; set; }

    [JsonPropertyName("approvedLocationRequired")]
    public bool ApprovedLocationRequired { get; set; }

    [JsonPropertyName("copyTypes")]
    public List<string> CopyTypes { get; set; } = [];
}

public sealed class SovDataResidency
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("approvedRegionsRequired")]
    public bool ApprovedRegionsRequired { get; set; }

    [JsonPropertyName("outsideScopeForbidden")]
    public bool OutsideScopeForbidden { get; set; }
}

public sealed class SovDataEgress
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("denyByDefault")]
    public bool DenyByDefault { get; set; }
}

public sealed class SovDataRemoteSupport
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("jitRequired")]
    public bool JitRequired { get; set; }

    [JsonPropertyName("loggedRequired")]
    public bool LoggedRequired { get; set; }

    [JsonPropertyName("approvalRequired")]
    public bool ApprovalRequired { get; set; }
}

public sealed class SovDataSubcontractors
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("inventoryRequired")]
    public bool InventoryRequired { get; set; }
}

public sealed class SovDataTelemetry
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("residencyBound")]
    public bool ResidencyBound { get; set; }
}

public sealed class SovDataDestruction
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("certificateRequired")]
    public bool CertificateRequired { get; set; }
}

public sealed class SovDataLawfulDisclosure
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("loggedDisclosureRequired")]
    public bool LoggedDisclosureRequired { get; set; }
}

public sealed class SovDataAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class SovDataMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveEgressForbidden")]
    public bool LiveEgressForbidden { get; set; }

    [JsonPropertyName("liveSupportJitForbidden")]
    public bool LiveSupportJitForbidden { get; set; }

    [JsonPropertyName("liveDeletionSystemForbidden")]
    public bool LiveDeletionSystemForbidden { get; set; }
}

public sealed class SovDataSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("dataSovereigntyPolicyVersion")]
    public string? DataSovereigntyPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class SovDataBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
