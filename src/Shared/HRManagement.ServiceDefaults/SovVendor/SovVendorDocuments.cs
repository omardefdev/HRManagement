using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.SovVendor;

public sealed class SovVendorMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("vendorExitPolicyVersion")]
    public string? VendorExitPolicyVersion { get; set; }

    [JsonPropertyName("ownership")]
    public SovVendorOwnership? Ownership { get; set; }

    [JsonPropertyName("escrowLicenses")]
    public SovVendorEscrowLicenses? EscrowLicenses { get; set; }

    [JsonPropertyName("openExport")]
    public SovVendorOpenExport? OpenExport { get; set; }

    [JsonPropertyName("criticalAlternates")]
    public SovVendorCriticalAlternates? CriticalAlternates { get; set; }

    [JsonPropertyName("cleanRoomRebuild")]
    public SovVendorCleanRoomRebuild? CleanRoomRebuild { get; set; }

    [JsonPropertyName("knowledgeTransfer")]
    public SovVendorKnowledgeTransfer? KnowledgeTransfer { get; set; }

    [JsonPropertyName("exitAssistance")]
    public SovVendorExitAssistance? ExitAssistance { get; set; }

    [JsonPropertyName("destructionAttestation")]
    public SovVendorDestructionAttestation? DestructionAttestation { get; set; }

    [JsonPropertyName("audit")]
    public SovVendorAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public SovVendorMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public SovVendorSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<SovVendorBlocker> Blockers { get; set; } = [];
}

public sealed class SovVendorOwnership
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("ipSourceBuildIacSbomDocsRequired")]
    public bool IpSourceBuildIacSbomDocsRequired { get; set; }
}

public sealed class SovVendorEscrowLicenses
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("escrowRequired")]
    public bool EscrowRequired { get; set; }

    [JsonPropertyName("licenseRightsRequired")]
    public bool LicenseRightsRequired { get; set; }

    [JsonPropertyName("subcontractorDisclosureRequired")]
    public bool SubcontractorDisclosureRequired { get; set; }
}

public sealed class SovVendorOpenExport
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("fullDataExportRequired")]
    public bool FullDataExportRequired { get; set; }

    [JsonPropertyName("attachmentsExportRequired")]
    public bool AttachmentsExportRequired { get; set; }

    [JsonPropertyName("contractsExportRequired")]
    public bool ContractsExportRequired { get; set; }

    [JsonPropertyName("documentedFormatsRequired")]
    public bool DocumentedFormatsRequired { get; set; }
}

public sealed class SovVendorCriticalAlternates
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("noCriticalWithoutAlternateOrException")]
    public bool NoCriticalWithoutAlternateOrException { get; set; }
}

public sealed class SovVendorCleanRoomRebuild
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("ownedArtifactsOnlyRequired")]
    public bool OwnedArtifactsOnlyRequired { get; set; }

    [JsonPropertyName("alternateSiteRequired")]
    public bool AlternateSiteRequired { get; set; }
}

public sealed class SovVendorKnowledgeTransfer
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class SovVendorExitAssistance
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class SovVendorDestructionAttestation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("priorVendorProofRequired")]
    public bool PriorVendorProofRequired { get; set; }
}

public sealed class SovVendorAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class SovVendorMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveCleanRoomRebuildForbidden")]
    public bool LiveCleanRoomRebuildForbidden { get; set; }

    [JsonPropertyName("liveVendorExitDrillForbidden")]
    public bool LiveVendorExitDrillForbidden { get; set; }

    [JsonPropertyName("liveFullExportImportForbidden")]
    public bool LiveFullExportImportForbidden { get; set; }

    [JsonPropertyName("liveVendorDestructionAttestationForbidden")]
    public bool LiveVendorDestructionAttestationForbidden { get; set; }
}

public sealed class SovVendorSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("vendorExitPolicyVersion")]
    public string? VendorExitPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class SovVendorBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
