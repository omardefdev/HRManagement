using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.MigCanonical;

public sealed class MigCanonicalMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("mappingVersion")]
    public string? MappingVersion { get; set; }

    [JsonPropertyName("allowedDispositions")]
    public List<string> AllowedDispositions { get; set; } = [];

    [JsonPropertyName("canonicalEntities")]
    public List<MigCanonicalEntity> CanonicalEntities { get; set; } = [];

    [JsonPropertyName("unmappedPolicy")]
    public MigUnmappedPolicy? UnmappedPolicy { get; set; }

    [JsonPropertyName("legacyIdPolicy")]
    public MigLegacyIdPolicy? LegacyIdPolicy { get; set; }

    [JsonPropertyName("localeCalendar")]
    public MigLocaleCalendar? LocaleCalendar { get; set; }

    [JsonPropertyName("signing")]
    public MigCodeMapSigning? Signing { get; set; }

    [JsonPropertyName("codeMaps")]
    public List<MigCodeMap> CodeMaps { get; set; } = [];

    [JsonPropertyName("blockers")]
    public List<MigCanonicalBlocker> Blockers { get; set; } = [];
}

public sealed class MigCanonicalEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

public sealed class MigUnmappedPolicy
{
    [JsonPropertyName("forbidOther")]
    public bool ForbidOther { get; set; }

    [JsonPropertyName("otherBucketMaxRate")]
    public double OtherBucketMaxRate { get; set; }

    [JsonPropertyName("quarantineQueueId")]
    public string? QuarantineQueueId { get; set; }

    [JsonPropertyName("forbiddenCatchAllValues")]
    public List<string> ForbiddenCatchAllValues { get; set; } = [];
}

public sealed class MigLegacyIdPolicy
{
    [JsonPropertyName("storeAsReferenceOnly")]
    public bool StoreAsReferenceOnly { get; set; }

    [JsonPropertyName("neverUseAsAuthOrTenantKey")]
    public bool NeverUseAsAuthOrTenantKey { get; set; }

    [JsonPropertyName("referenceFields")]
    public List<string> ReferenceFields { get; set; } = [];

    [JsonPropertyName("platformKey")]
    public string? PlatformKey { get; set; }
}

public sealed class MigLocaleCalendar
{
    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }
}

public sealed class MigCodeMapSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("mappingVersion")]
    public string? MappingVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class MigCodeMap
{
    [JsonPropertyName("mapId")]
    public string MapId { get; set; } = "";

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("sourceField")]
    public string? SourceField { get; set; }

    [JsonPropertyName("sourceValue")]
    public string? SourceValue { get; set; }

    [JsonPropertyName("targetField")]
    public string? TargetField { get; set; }

    [JsonPropertyName("targetValue")]
    public string? TargetValue { get; set; }

    [JsonPropertyName("disposition")]
    public string? Disposition { get; set; }
}

public sealed class MigCanonicalBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
