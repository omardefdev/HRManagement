using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ExtSector;

public sealed class ExtSectorMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("sectorCatalogPolicyVersion")]
    public string? SectorCatalogPolicyVersion { get; set; }

    [JsonPropertyName("noImplicitCriticalIntegrations")]
    public bool NoImplicitCriticalIntegrations { get; set; }

    [JsonPropertyName("adapterRequirements")]
    public ExtSectorAdapterRequirements? AdapterRequirements { get; set; }

    [JsonPropertyName("notification")]
    public ExtSectorNotification? Notification { get; set; }

    [JsonPropertyName("failure")]
    public ExtSectorFailure? Failure { get; set; }

    [JsonPropertyName("certification")]
    public ExtSectorCertification? Certification { get; set; }

    [JsonPropertyName("reconciliation")]
    public ExtSectorReconciliation? Reconciliation { get; set; }

    [JsonPropertyName("deliveryReceipts")]
    public ExtSectorDeliveryReceipts? DeliveryReceipts { get; set; }

    [JsonPropertyName("catalogDomains")]
    public List<ExtSectorCatalogDomain> CatalogDomains { get; set; } = [];

    [JsonPropertyName("audit")]
    public ExtSectorAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ExtSectorMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ExtSectorSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ExtSectorBlocker> Blockers { get; set; } = [];
}

public sealed class ExtSectorAdapterRequirements
{
    [JsonPropertyName("contractRequired")]
    public bool ContractRequired { get; set; }

    [JsonPropertyName("sloRequired")]
    public bool SloRequired { get; set; }

    [JsonPropertyName("retryRequired")]
    public bool RetryRequired { get; set; }

    [JsonPropertyName("reconciliationRequired")]
    public bool ReconciliationRequired { get; set; }

    [JsonPropertyName("exitRequired")]
    public bool ExitRequired { get; set; }
}

public sealed class ExtSectorNotification
{
    [JsonPropertyName("sensitivePiiForbidden")]
    public bool SensitivePiiForbidden { get; set; }
}

public sealed class ExtSectorFailure
{
    [JsonPropertyName("mustSurface")]
    public bool MustSurface { get; set; }

    [JsonPropertyName("swallowForbidden")]
    public bool SwallowForbidden { get; set; }
}

public sealed class ExtSectorCertification
{
    [JsonPropertyName("requiredBeforeEnable")]
    public bool RequiredBeforeEnable { get; set; }
}

public sealed class ExtSectorReconciliation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ExtSectorDeliveryReceipts
{
    [JsonPropertyName("requiredWhereApplicable")]
    public bool RequiredWhereApplicable { get; set; }
}

public sealed class ExtSectorCatalogDomain
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }

    [JsonPropertyName("sourceOfTruth")]
    public string? SourceOfTruth { get; set; }

    [JsonPropertyName("latencyClass")]
    public string? LatencyClass { get; set; }

    [JsonPropertyName("ownerRole")]
    public string? OwnerRole { get; set; }

    [JsonPropertyName("fallback")]
    public string? Fallback { get; set; }
}

public sealed class ExtSectorAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ExtSectorMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveAdaptersForbidden")]
    public bool LiveAdaptersForbidden { get; set; }

    [JsonPropertyName("liveNotificationForbidden")]
    public bool LiveNotificationForbidden { get; set; }
}

public sealed class ExtSectorSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("sectorCatalogPolicyVersion")]
    public string? SectorCatalogPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ExtSectorBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
