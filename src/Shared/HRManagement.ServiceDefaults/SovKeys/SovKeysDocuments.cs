using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.SovKeys;

public sealed class SovKeysMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("keySovereigntyPolicyVersion")]
    public string? KeySovereigntyPolicyVersion { get; set; }

    [JsonPropertyName("keyHierarchy")]
    public SovKeysHierarchy? KeyHierarchy { get; set; }

    [JsonPropertyName("hsmKms")]
    public SovKeysHsmKms? HsmKms { get; set; }

    [JsonPropertyName("byokHyok")]
    public SovKeysByokHyok? ByokHyok { get; set; }

    [JsonPropertyName("separation")]
    public SovKeysSeparation? Separation { get; set; }

    [JsonPropertyName("dualControl")]
    public SovKeysRequiredFlag? DualControl { get; set; }

    [JsonPropertyName("rotation")]
    public SovKeysRotation? Rotation { get; set; }

    [JsonPropertyName("algorithmAgility")]
    public SovKeysAlgorithmAgility? AlgorithmAgility { get; set; }

    [JsonPropertyName("tlsCertificates")]
    public SovKeysTlsCertificates? TlsCertificates { get; set; }

    [JsonPropertyName("trustedTime")]
    public SovKeysTrustedTime? TrustedTime { get; set; }

    [JsonPropertyName("audit")]
    public SovKeysAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public SovKeysMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public SovKeysSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<SovKeysBlocker> Blockers { get; set; } = [];
}

public sealed class SovKeysRequiredFlag
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class SovKeysHierarchy
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("inventoryRequired")]
    public bool InventoryRequired { get; set; }
}

public sealed class SovKeysHsmKms
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("approvedCustodyRequired")]
    public bool ApprovedCustodyRequired { get; set; }

    [JsonPropertyName("vendorAloneCannotDecrypt")]
    public bool VendorAloneCannotDecrypt { get; set; }

    [JsonPropertyName("labMockAllowed")]
    public bool LabMockAllowed { get; set; }
}

public sealed class SovKeysByokHyok
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("whereRequiredFlag")]
    public bool WhereRequiredFlag { get; set; }
}

public sealed class SovKeysSeparation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("envTenantPurposeSeparation")]
    public bool EnvTenantPurposeSeparation { get; set; }
}

public sealed class SovKeysRotation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("revocationRequired")]
    public bool RevocationRequired { get; set; }

    [JsonPropertyName("backupRequired")]
    public bool BackupRequired { get; set; }

    [JsonPropertyName("destructionRequired")]
    public bool DestructionRequired { get; set; }

    [JsonPropertyName("custodianLossDrillRequired")]
    public bool CustodianLossDrillRequired { get; set; }
}

public sealed class SovKeysAlgorithmAgility
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("deprecatedAlgForbiddenInProdClaim")]
    public bool DeprecatedAlgForbiddenInProdClaim { get; set; }
}

public sealed class SovKeysTlsCertificates
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("inventoryExpiryAlertsRequired")]
    public bool InventoryExpiryAlertsRequired { get; set; }
}

public sealed class SovKeysTrustedTime
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("clockDriftBoundRequired")]
    public bool ClockDriftBoundRequired { get; set; }
}

public sealed class SovKeysAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class SovKeysMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveHsmForbidden")]
    public bool LiveHsmForbidden { get; set; }

    [JsonPropertyName("liveKeyCeremonyForbidden")]
    public bool LiveKeyCeremonyForbidden { get; set; }

    [JsonPropertyName("liveTrustedTimeForbidden")]
    public bool LiveTrustedTimeForbidden { get; set; }
}

public sealed class SovKeysSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("keySovereigntyPolicyVersion")]
    public string? KeySovereigntyPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class SovKeysBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
