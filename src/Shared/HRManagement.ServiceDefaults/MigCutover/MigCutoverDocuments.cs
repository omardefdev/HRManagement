using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.MigCutover;

public sealed class MigCutoverMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("cutoverPolicyVersion")]
    public string? CutoverPolicyVersion { get; set; }

    [JsonPropertyName("minRehearsals")]
    public int MinRehearsals { get; set; }

    [JsonPropertyName("rehearsals")]
    public List<MigCutoverRehearsal> Rehearsals { get; set; } = [];

    [JsonPropertyName("changeFreeze")]
    public MigChangeFreezePolicy? ChangeFreeze { get; set; }

    [JsonPropertyName("deltaCapture")]
    public MigDeltaCapturePolicy? DeltaCapture { get; set; }

    [JsonPropertyName("parallelMode")]
    public MigParallelModePolicy? ParallelMode { get; set; }

    [JsonPropertyName("userLock")]
    public MigUserLockPolicy? UserLock { get; set; }

    [JsonPropertyName("dnsCutover")]
    public MigDnsCutoverPolicy? DnsCutover { get; set; }

    [JsonPropertyName("communicationPlan")]
    public MigCommunicationPlanPolicy? CommunicationPlan { get; set; }

    [JsonPropertyName("rollback")]
    public MigRollbackPolicy? Rollback { get; set; }

    [JsonPropertyName("goNoGo")]
    public MigGoNoGoPolicy? GoNoGo { get; set; }

    [JsonPropertyName("legacyAccess")]
    public MigLegacyAccessPolicy? LegacyAccess { get; set; }

    [JsonPropertyName("postCutoverRecon")]
    public MigPostCutoverReconPolicy? PostCutoverRecon { get; set; }

    [JsonPropertyName("signing")]
    public MigCutoverSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<MigCutoverBlocker> Blockers { get; set; } = [];
}

public sealed class MigCutoverRehearsal
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("timed")]
    public bool Timed { get; set; }

    [JsonPropertyName("windowHours")]
    public double WindowHours { get; set; }

    [JsonPropertyName("volumeRef")]
    public string? VolumeRef { get; set; }

    [JsonPropertyName("volumeOk")]
    public bool VolumeOk { get; set; }

    [JsonPropertyName("withinWindow")]
    public bool WithinWindow { get; set; }
}

public sealed class MigChangeFreezePolicy
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("join")]
    public string? Join { get; set; }
}

public sealed class MigDeltaCapturePolicy
{
    [JsonPropertyName("noLossRequired")]
    public bool NoLossRequired { get; set; }

    [JsonPropertyName("captureMode")]
    public string? CaptureMode { get; set; }
}

public sealed class MigParallelModePolicy
{
    [JsonPropertyName("read")]
    public bool Read { get; set; }

    [JsonPropertyName("run")]
    public bool Run { get; set; }
}

public sealed class MigUserLockPolicy
{
    [JsonPropertyName("requiredBeforeCutover")]
    public bool RequiredBeforeCutover { get; set; }
}

public sealed class MigDnsCutoverPolicy
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveForbiddenInThisPack")]
    public bool LiveForbiddenInThisPack { get; set; }
}

public sealed class MigCommunicationPlanPolicy
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("channels")]
    public List<string> Channels { get; set; } = [];
}

public sealed class MigRollbackPolicy
{
    [JsonPropertyName("windowHours")]
    public double WindowHours { get; set; }

    [JsonPropertyName("mustBeExercised")]
    public bool MustBeExercised { get; set; }

    [JsonPropertyName("paperOnlyForbidden")]
    public bool PaperOnlyForbidden { get; set; }

    [JsonPropertyName("ops003Join")]
    public string? Ops003Join { get; set; }
}

public sealed class MigGoNoGoPolicy
{
    [JsonPropertyName("decisionRequired")]
    public bool DecisionRequired { get; set; }

    [JsonPropertyName("criteria")]
    public List<string> Criteria { get; set; } = [];
}

public sealed class MigLegacyAccessPolicy
{
    [JsonPropertyName("readOnlyAfterCutover")]
    public bool ReadOnlyAfterCutover { get; set; }

    [JsonPropertyName("securedRequired")]
    public bool SecuredRequired { get; set; }
}

public sealed class MigPostCutoverReconPolicy
{
    [JsonPropertyName("checkpoints")]
    public List<string> Checkpoints { get; set; } = [];

    [JsonPropertyName("mig005Join")]
    public string? Mig005Join { get; set; }
}

public sealed class MigCutoverSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("cutoverPolicyVersion")]
    public string? CutoverPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class MigCutoverBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
