using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ProdWorkbox;

public sealed class ProdWorkboxMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("workboxPolicyVersion")]
    public string? WorkboxPolicyVersion { get; set; }

    [JsonPropertyName("queueCompleteness")]
    public ProdWorkboxQueueCompleteness? QueueCompleteness { get; set; }

    [JsonPropertyName("prioritySlaEscalation")]
    public ProdWorkboxPrioritySlaEscalation? PrioritySlaEscalation { get; set; }

    [JsonPropertyName("safeSearch")]
    public ProdWorkboxSafeSearch? SafeSearch { get; set; }

    [JsonPropertyName("bulkActions")]
    public ProdWorkboxBulkActions? BulkActions { get; set; }

    [JsonPropertyName("notifications")]
    public ProdWorkboxNotifications? Notifications { get; set; }

    [JsonPropertyName("deepLinks")]
    public ProdWorkboxDeepLinks? DeepLinks { get; set; }

    [JsonPropertyName("audit")]
    public ProdWorkboxAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ProdWorkboxMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ProdWorkboxSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ProdWorkboxBlocker> Blockers { get; set; } = [];
}

public sealed class ProdWorkboxQueueCompleteness
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("noLostCrossModule")]
    public bool NoLostCrossModule { get; set; }

    [JsonPropertyName("itemTypes")]
    public List<string> ItemTypes { get; set; } = [];
}

public sealed class ProdWorkboxPrioritySlaEscalation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdWorkboxSafeSearch
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("tenantScoped")]
    public bool TenantScoped { get; set; }

    [JsonPropertyName("orgScoped")]
    public bool OrgScoped { get; set; }

    [JsonPropertyName("existenceLeakForbidden")]
    public bool ExistenceLeakForbidden { get; set; }
}

public sealed class ProdWorkboxBulkActions
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("limitsRequired")]
    public bool LimitsRequired { get; set; }

    [JsonPropertyName("fourEyesRequired")]
    public bool FourEyesRequired { get; set; }
}

public sealed class ProdWorkboxNotifications
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("noSecretsInPayload")]
    public bool NoSecretsInPayload { get; set; }

    [JsonPropertyName("deliveryReceiptsRequired")]
    public bool DeliveryReceiptsRequired { get; set; }

    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("liveForbidden")]
    public bool LiveForbidden { get; set; }
}

public sealed class ProdWorkboxDeepLinks
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("reauthOnExpiredRequired")]
    public bool ReauthOnExpiredRequired { get; set; }

    [JsonPropertyName("correctStateRequired")]
    public bool CorrectStateRequired { get; set; }
}

public sealed class ProdWorkboxAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ProdWorkboxMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveWorkboxForbidden")]
    public bool LiveWorkboxForbidden { get; set; }

    [JsonPropertyName("liveNotificationForbidden")]
    public bool LiveNotificationForbidden { get; set; }
}

public sealed class ProdWorkboxSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("workboxPolicyVersion")]
    public string? WorkboxPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ProdWorkboxBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
