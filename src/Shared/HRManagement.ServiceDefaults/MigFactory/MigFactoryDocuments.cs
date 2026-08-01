using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.MigFactory;

public sealed class MigFactoryMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("pipelineVersion")]
    public string? PipelineVersion { get; set; }

    [JsonPropertyName("stages")]
    public List<MigFactoryStage> Stages { get; set; } = [];

    [JsonPropertyName("stagingZones")]
    public List<MigStagingZone> StagingZones { get; set; } = [];

    [JsonPropertyName("idempotency")]
    public MigIdempotencyPolicy? Idempotency { get; set; }

    [JsonPropertyName("checkpoint")]
    public MigCheckpointPolicy? Checkpoint { get; set; }

    [JsonPropertyName("lineage")]
    public MigLineagePolicy? Lineage { get; set; }

    [JsonPropertyName("piiLogging")]
    public MigPiiLoggingPolicy? PiiLogging { get; set; }

    [JsonPropertyName("throughput")]
    public MigThroughputPolicy? Throughput { get; set; }

    [JsonPropertyName("secrets")]
    public MigSecretsPolicy? Secrets { get; set; }

    [JsonPropertyName("pipelines")]
    public List<MigPipelineDefinition> Pipelines { get; set; } = [];

    [JsonPropertyName("blockers")]
    public List<MigFactoryBlocker> Blockers { get; set; } = [];
}

public sealed class MigFactoryStage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("checkpointable")]
    public bool Checkpointable { get; set; }
}

public sealed class MigStagingZone
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("encryptionAtRest")]
    public bool EncryptionAtRest { get; set; }

    [JsonPropertyName("accessRole")]
    public string? AccessRole { get; set; }

    [JsonPropertyName("tenantScoped")]
    public bool TenantScoped { get; set; }

    [JsonPropertyName("purgePolicy")]
    public string? PurgePolicy { get; set; }
}

public sealed class MigIdempotencyPolicy
{
    [JsonPropertyName("batchKeyFields")]
    public List<string> BatchKeyFields { get; set; } = [];

    [JsonPropertyName("naturalKeyFields")]
    public List<string> NaturalKeyFields { get; set; } = [];

    [JsonPropertyName("reloadPolicy")]
    public string? ReloadPolicy { get; set; }

    [JsonPropertyName("duplicateOnReloadForbidden")]
    public bool DuplicateOnReloadForbidden { get; set; }
}

public sealed class MigCheckpointPolicy
{
    [JsonPropertyName("store")]
    public string? Store { get; set; }

    [JsonPropertyName("resumeFromStage")]
    public bool ResumeFromStage { get; set; }

    [JsonPropertyName("midFailBehavior")]
    public string? MidFailBehavior { get; set; }
}

public sealed class MigLineagePolicy
{
    [JsonPropertyName("perRecordFields")]
    public List<string> PerRecordFields { get; set; } = [];
}

public sealed class MigPiiLoggingPolicy
{
    [JsonPropertyName("forbiddenFields")]
    public List<string> ForbiddenFields { get; set; } = [];

    [JsonPropertyName("redactionMode")]
    public string? RedactionMode { get; set; }

    [JsonPropertyName("logSamplePolicy")]
    public string? LogSamplePolicy { get; set; }
}

public sealed class MigThroughputPolicy
{
    [JsonPropertyName("cutoverWindowHours")]
    public double CutoverWindowHours { get; set; }

    [JsonPropertyName("targetRowsPerHour")]
    public double TargetRowsPerHour { get; set; }

    [JsonPropertyName("syntheticBudgetRef")]
    public string? SyntheticBudgetRef { get; set; }

    [JsonPropertyName("liveProofRequired")]
    public bool LiveProofRequired { get; set; }
}

public sealed class MigSecretsPolicy
{
    [JsonPropertyName("store")]
    public string? Store { get; set; }

    [JsonPropertyName("rotateBeforeWave")]
    public bool RotateBeforeWave { get; set; }

    [JsonPropertyName("noSecretsInLogs")]
    public bool NoSecretsInLogs { get; set; }
}

public sealed class MigPipelineDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("sourceIds")]
    public List<string> SourceIds { get; set; } = [];

    [JsonPropertyName("codeMapVersionRef")]
    public string? CodeMapVersionRef { get; set; }

    [JsonPropertyName("stageIds")]
    public List<string> StageIds { get; set; } = [];
}

public sealed class MigFactoryBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
