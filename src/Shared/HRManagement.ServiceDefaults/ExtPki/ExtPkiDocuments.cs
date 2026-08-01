using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ExtPki;

public sealed class ExtPkiMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("pkiPolicyVersion")]
    public string? PkiPolicyVersion { get; set; }

    [JsonPropertyName("signerIntentRequired")]
    public bool SignerIntentRequired { get; set; }

    [JsonPropertyName("signerRoleRequired")]
    public bool SignerRoleRequired { get; set; }

    [JsonPropertyName("signatures")]
    public ExtPkiSignatures? Signatures { get; set; }

    [JsonPropertyName("organizationalSeals")]
    public ExtPkiOrganizationalSeals? OrganizationalSeals { get; set; }

    [JsonPropertyName("trustedTimestamp")]
    public ExtPkiTrustedTimestamp? TrustedTimestamp { get; set; }

    [JsonPropertyName("certValidation")]
    public ExtPkiCertValidation? CertValidation { get; set; }

    [JsonPropertyName("revocation")]
    public ExtPkiRevocation? Revocation { get; set; }

    [JsonPropertyName("ltv")]
    public ExtPkiLtv? Ltv { get; set; }

    [JsonPropertyName("hsmCustody")]
    public ExtPkiHsmCustody? HsmCustody { get; set; }

    [JsonPropertyName("clockSkewPolicy")]
    public ExtPkiClockSkewPolicy? ClockSkewPolicy { get; set; }

    [JsonPropertyName("algorithmAgility")]
    public ExtPkiAlgorithmAgility? AlgorithmAgility { get; set; }

    [JsonPropertyName("audit")]
    public ExtPkiAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ExtPkiMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ExtPkiSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ExtPkiBlocker> Blockers { get; set; } = [];
}

public sealed class ExtPkiSignatures
{
    [JsonPropertyName("kinds")]
    public List<string> Kinds { get; set; } = [];

    [JsonPropertyName("formSignatureNotLegalEvidence")]
    public bool FormSignatureNotLegalEvidence { get; set; }
}

public sealed class ExtPkiOrganizationalSeals
{
    [JsonPropertyName("cryptoSealRequiredForLegal")]
    public bool CryptoSealRequiredForLegal { get; set; }

    [JsonPropertyName("metadataOnlyInsufficient")]
    public bool MetadataOnlyInsufficient { get; set; }
}

public sealed class ExtPkiTrustedTimestamp
{
    [JsonPropertyName("requiredForLtv")]
    public bool RequiredForLtv { get; set; }

    [JsonPropertyName("tsaOutageDegrade")]
    public string TsaOutageDegrade { get; set; } = "";
}

public sealed class ExtPkiCertValidation
{
    [JsonPropertyName("checkExpiry")]
    public bool CheckExpiry { get; set; }

    [JsonPropertyName("checkRevocation")]
    public bool CheckRevocation { get; set; }

    [JsonPropertyName("failClosedOnUnknown")]
    public bool FailClosedOnUnknown { get; set; }
}

public sealed class ExtPkiRevocation
{
    [JsonPropertyName("failClosed")]
    public bool FailClosed { get; set; }

    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = [];
}

public sealed class ExtPkiLtv
{
    [JsonPropertyName("requiredAfterExpiry")]
    public bool RequiredAfterExpiry { get; set; }

    [JsonPropertyName("archiveEvidenceRequired")]
    public bool ArchiveEvidenceRequired { get; set; }
}

public sealed class ExtPkiHsmCustody
{
    [JsonPropertyName("privateKeyNeverLeavesHsm")]
    public bool PrivateKeyNeverLeavesHsm { get; set; }

    [JsonPropertyName("labMockAllowed")]
    public bool LabMockAllowed { get; set; }
}

public sealed class ExtPkiClockSkewPolicy
{
    [JsonPropertyName("maxSkewSeconds")]
    public int MaxSkewSeconds { get; set; }

    [JsonPropertyName("failClosed")]
    public bool FailClosed { get; set; }
}

public sealed class ExtPkiAlgorithmAgility
{
    [JsonPropertyName("allowedAlgs")]
    public List<string> AllowedAlgs { get; set; } = [];

    [JsonPropertyName("deprecatedAlgs")]
    public List<string> DeprecatedAlgs { get; set; } = [];
}

public sealed class ExtPkiAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ExtPkiMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("livePkiForbidden")]
    public bool LivePkiForbidden { get; set; }

    [JsonPropertyName("mockCa")]
    public bool MockCa { get; set; }

    [JsonPropertyName("mockTsa")]
    public bool MockTsa { get; set; }

    [JsonPropertyName("mockHsm")]
    public bool MockHsm { get; set; }
}

public sealed class ExtPkiSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("pkiPolicyVersion")]
    public string? PkiPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ExtPkiBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
