using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.MigInventory;

public sealed class MigInventoryMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("knownSourceUniverse")]
    public List<string> KnownSourceUniverse { get; set; } = [];

    [JsonPropertyName("requiredTargetFields")]
    public List<string> RequiredTargetFields { get; set; } = [];

    [JsonPropertyName("requiredTargetFieldSotFields")]
    public List<string> RequiredTargetFieldSotFields { get; set; } = [];

    [JsonPropertyName("sources")]
    public List<MigSource> Sources { get; set; } = [];

    [JsonPropertyName("targetFieldSot")]
    public List<MigFieldSot> TargetFieldSot { get; set; } = [];

    [JsonPropertyName("exceptions")]
    public List<MigInventoryException> Exceptions { get; set; } = [];

    [JsonPropertyName("blockers")]
    public List<MigInventoryBlocker> Blockers { get; set; } = [];
}

public sealed class MigSource
{
    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = "";

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("accessRisks")]
    public List<string> AccessRisks { get; set; } = [];

    [JsonPropertyName("volume")]
    public MigSourceVolume? Volume { get; set; }
}

public sealed class MigSourceVolume
{
    [JsonPropertyName("estimateOnly")]
    public bool EstimateOnly { get; set; }

    [JsonPropertyName("measuredAt")]
    public string? MeasuredAt { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("rowCount")]
    public long? RowCount { get; set; }

    [JsonPropertyName("fileCount")]
    public long? FileCount { get; set; }
}

public sealed class MigFieldSot
{
    [JsonPropertyName("targetField")]
    public string TargetField { get; set; } = "";

    [JsonPropertyName("sourceOfTruthSourceId")]
    public string? SourceOfTruthSourceId { get; set; }

    [JsonPropertyName("sourceField")]
    public string? SourceField { get; set; }

    [JsonPropertyName("gov003Join")]
    public string? Gov003Join { get; set; }
}

public sealed class MigInventoryException
{
    [JsonPropertyName("exceptionId")]
    public string ExceptionId { get; set; } = "";

    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = "";

    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("expiresOn")]
    public string? ExpiresOn { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class MigInventoryBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
