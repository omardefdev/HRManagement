using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ExtApi;

public sealed class ExtApiMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("apiExchangePolicyVersion")]
    public string? ApiExchangePolicyVersion { get; set; }

    [JsonPropertyName("catalogRequired")]
    public bool CatalogRequired { get; set; }

    [JsonPropertyName("requiredCatalogFields")]
    public List<string> RequiredCatalogFields { get; set; } = [];

    [JsonPropertyName("versioning")]
    public ExtApiVersioning? Versioning { get; set; }

    [JsonPropertyName("compatibility")]
    public ExtApiCompatibility? Compatibility { get; set; }

    [JsonPropertyName("sharedDbForbidden")]
    public bool SharedDbForbidden { get; set; }

    [JsonPropertyName("purposeBoundExport")]
    public ExtApiPurposeBoundExport? PurposeBoundExport { get; set; }

    [JsonPropertyName("throttling")]
    public ExtApiThrottling? Throttling { get; set; }

    [JsonPropertyName("nonRepudiation")]
    public ExtApiNonRepudiation? NonRepudiation { get; set; }

    [JsonPropertyName("tenant")]
    public ExtApiTenant? Tenant { get; set; }

    [JsonPropertyName("errorModel")]
    public ExtApiErrorModel? ErrorModel { get; set; }

    [JsonPropertyName("deprecation")]
    public ExtApiDeprecation? Deprecation { get; set; }

    [JsonPropertyName("dataMinimization")]
    public ExtApiDataMinimization? DataMinimization { get; set; }

    [JsonPropertyName("schemaRegistry")]
    public ExtApiSchemaRegistry? SchemaRegistry { get; set; }

    [JsonPropertyName("consumerDriven")]
    public ExtApiConsumerDriven? ConsumerDriven { get; set; }

    [JsonPropertyName("audit")]
    public ExtApiAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ExtApiMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ExtApiSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ExtApiBlocker> Blockers { get; set; } = [];
}

public sealed class ExtApiVersioning
{
    [JsonPropertyName("semverRequired")]
    public bool SemverRequired { get; set; }

    [JsonPropertyName("breakingRequiresMajorBump")]
    public bool BreakingRequiresMajorBump { get; set; }
}

public sealed class ExtApiCompatibility
{
    [JsonPropertyName("measureBackwardCompat")]
    public bool MeasureBackwardCompat { get; set; }

    [JsonPropertyName("supportNMinus1")]
    public bool SupportNMinus1 { get; set; }
}

public sealed class ExtApiPurposeBoundExport
{
    [JsonPropertyName("consentRequired")]
    public bool ConsentRequired { get; set; }

    [JsonPropertyName("purposeAllowlist")]
    public List<string> PurposeAllowlist { get; set; } = [];
}

public sealed class ExtApiThrottling
{
    [JsonPropertyName("defaultRpm")]
    public int DefaultRpm { get; set; }

    [JsonPropertyName("burst")]
    public int Burst { get; set; }

    [JsonPropertyName("onExceed")]
    public int OnExceed { get; set; }
}

public sealed class ExtApiNonRepudiation
{
    [JsonPropertyName("correlationIdRequired")]
    public bool CorrelationIdRequired { get; set; }

    [JsonPropertyName("auditRequired")]
    public bool AuditRequired { get; set; }
}

public sealed class ExtApiTenant
{
    [JsonPropertyName("tenantHeaderRequired")]
    public bool TenantHeaderRequired { get; set; }

    [JsonPropertyName("crossTenantDeny")]
    public bool CrossTenantDeny { get; set; }
}

public sealed class ExtApiErrorModel
{
    [JsonPropertyName("problemDetailsRequired")]
    public bool ProblemDetailsRequired { get; set; }
}

public sealed class ExtApiDeprecation
{
    [JsonPropertyName("noticeDaysMin")]
    public int NoticeDaysMin { get; set; }

    [JsonPropertyName("sunsetRequiresMajor")]
    public bool SunsetRequiresMajor { get; set; }
}

public sealed class ExtApiDataMinimization
{
    [JsonPropertyName("forbidExcessFields")]
    public bool ForbidExcessFields { get; set; }
}

public sealed class ExtApiSchemaRegistry
{
    [JsonPropertyName("labMockAllowed")]
    public bool LabMockAllowed { get; set; }

    [JsonPropertyName("liveForbidden")]
    public bool LiveForbidden { get; set; }
}

public sealed class ExtApiConsumerDriven
{
    [JsonPropertyName("certifiedConsumersProtected")]
    public bool CertifiedConsumersProtected { get; set; }
}

public sealed class ExtApiAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ExtApiMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveBusForbidden")]
    public bool LiveBusForbidden { get; set; }

    [JsonPropertyName("mockCatalog")]
    public bool MockCatalog { get; set; }

    [JsonPropertyName("mockRegistry")]
    public bool MockRegistry { get; set; }
}

public sealed class ExtApiSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("apiExchangePolicyVersion")]
    public string? ApiExchangePolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ExtApiBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
