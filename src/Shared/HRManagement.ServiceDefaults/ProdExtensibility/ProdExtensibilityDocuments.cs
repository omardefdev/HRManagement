using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ProdExtensibility;

public sealed class ProdExtensibilityMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("extensibilityPolicyVersion")]
    public string? ExtensibilityPolicyVersion { get; set; }

    [JsonPropertyName("supportedContracts")]
    public ProdExtensibilitySupportedContracts? SupportedContracts { get; set; }

    [JsonPropertyName("semanticVersioning")]
    public ProdExtensibilitySemanticVersioning? SemanticVersioning { get; set; }

    [JsonPropertyName("compatibilityWindows")]
    public ProdExtensibilityCompatibilityWindows? CompatibilityWindows { get; set; }

    [JsonPropertyName("pluginIsolation")]
    public ProdExtensibilityPluginIsolation? PluginIsolation { get; set; }

    [JsonPropertyName("quotas")]
    public ProdExtensibilityQuotas? Quotas { get; set; }

    [JsonPropertyName("certification")]
    public ProdExtensibilityCertification? Certification { get; set; }

    [JsonPropertyName("deprecation")]
    public ProdExtensibilityDeprecation? Deprecation { get; set; }

    [JsonPropertyName("sdkPolicy")]
    public ProdExtensibilitySdkPolicy? SdkPolicy { get; set; }

    [JsonPropertyName("noDirectDb")]
    public bool NoDirectDb { get; set; }

    [JsonPropertyName("noCustomFork")]
    public bool NoCustomFork { get; set; }

    [JsonPropertyName("audit")]
    public ProdExtensibilityAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ProdExtensibilityMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ProdExtensibilitySigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ProdExtensibilityBlocker> Blockers { get; set; } = [];
}

public sealed class ProdExtensibilitySupportedContracts
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("contractTypes")]
    public List<string> ContractTypes { get; set; } = [];
}

public sealed class ProdExtensibilitySemanticVersioning
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("semverRequired")]
    public bool SemverRequired { get; set; }

    [JsonPropertyName("breakingRequiresMajorBump")]
    public bool BreakingRequiresMajorBump { get; set; }
}

public sealed class ProdExtensibilityCompatibilityWindows
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("supportNMinus2")]
    public bool SupportNMinus2 { get; set; }

    [JsonPropertyName("measureBackwardCompat")]
    public bool MeasureBackwardCompat { get; set; }
}

public sealed class ProdExtensibilityPluginIsolation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("crossTenantForbidden")]
    public bool CrossTenantForbidden { get; set; }

    [JsonPropertyName("coreDisruptionForbidden")]
    public bool CoreDisruptionForbidden { get; set; }
}

public sealed class ProdExtensibilityQuotas
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("enforceRpm")]
    public bool EnforceRpm { get; set; }
}

public sealed class ProdExtensibilityCertification
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("consumerInventoryRequired")]
    public bool ConsumerInventoryRequired { get; set; }

    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("liveForbidden")]
    public bool LiveForbidden { get; set; }
}

public sealed class ProdExtensibilityDeprecation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("noticeDaysMin")]
    public int NoticeDaysMin { get; set; }

    [JsonPropertyName("measuredAnnouncementRequired")]
    public bool MeasuredAnnouncementRequired { get; set; }
}

public sealed class ProdExtensibilitySdkPolicy
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("liveSdkForbidden")]
    public bool LiveSdkForbidden { get; set; }
}

public sealed class ProdExtensibilityAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ProdExtensibilityMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveExtensionRuntimeForbidden")]
    public bool LiveExtensionRuntimeForbidden { get; set; }

    [JsonPropertyName("livePartnerSdkForbidden")]
    public bool LivePartnerSdkForbidden { get; set; }

    [JsonPropertyName("liveWebhookDeliveryForbidden")]
    public bool LiveWebhookDeliveryForbidden { get; set; }
}

public sealed class ProdExtensibilitySigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("extensibilityPolicyVersion")]
    public string? ExtensibilityPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ProdExtensibilityBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
