using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.MigMatch;

public sealed class MigMatchMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("matchPolicyVersion")]
    public string? MatchPolicyVersion { get; set; }

    [JsonPropertyName("matchStrategies")]
    public List<MigMatchStrategy> MatchStrategies { get; set; } = [];

    [JsonPropertyName("nationalIdExceptions")]
    public List<MigNationalIdException> NationalIdExceptions { get; set; } = [];

    [JsonPropertyName("thresholds")]
    public MigMatchThresholds? Thresholds { get; set; }

    [JsonPropertyName("falsePositiveControls")]
    public MigFalsePositiveControls? FalsePositiveControls { get; set; }

    [JsonPropertyName("humanReview")]
    public MigHumanReviewPolicy? HumanReview { get; set; }

    [JsonPropertyName("survivorRules")]
    public MigSurvivorRules? SurvivorRules { get; set; }

    [JsonPropertyName("fraudIndicators")]
    public List<MigFraudIndicator> FraudIndicators { get; set; } = [];

    [JsonPropertyName("mergeAudit")]
    public MigMergeAuditPolicy? MergeAudit { get; set; }

    [JsonPropertyName("reviewPrivacy")]
    public MigReviewPrivacyPolicy? ReviewPrivacy { get; set; }

    [JsonPropertyName("signing")]
    public MigMatchSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<MigMatchBlocker> Blockers { get; set; } = [];
}

public sealed class MigMatchStrategy
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("defaultOutcome")]
    public string? DefaultOutcome { get; set; }

    [JsonPropertyName("autoMergeAllowedWhen")]
    public string? AutoMergeAllowedWhen { get; set; }
}

public sealed class MigNationalIdException
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("autoMergeForbidden")]
    public bool AutoMergeForbidden { get; set; }

    [JsonPropertyName("route")]
    public string? Route { get; set; }
}

public sealed class MigMatchThresholds
{
    [JsonPropertyName("autoMergeMaxRisk")]
    public string? AutoMergeMaxRisk { get; set; }

    [JsonPropertyName("reviewMinScore")]
    public double ReviewMinScore { get; set; }

    [JsonPropertyName("rejectBelowScore")]
    public double RejectBelowScore { get; set; }

    [JsonPropertyName("precisionTarget")]
    public double PrecisionTarget { get; set; }

    [JsonPropertyName("recallTarget")]
    public double RecallTarget { get; set; }

    [JsonPropertyName("highRiskAutoMergeForbidden")]
    public bool HighRiskAutoMergeForbidden { get; set; }
}

public sealed class MigFalsePositiveControls
{
    [JsonPropertyName("requireEvidenceForAutoMerge")]
    public bool RequireEvidenceForAutoMerge { get; set; }

    [JsonPropertyName("secondReviewerForHighRisk")]
    public bool SecondReviewerForHighRisk { get; set; }

    [JsonPropertyName("maxAutoMergePerBatch")]
    public int MaxAutoMergePerBatch { get; set; }
}

public sealed class MigHumanReviewPolicy
{
    [JsonPropertyName("queue")]
    public string? Queue { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = [];

    [JsonPropertyName("slaHours")]
    public int SlaHours { get; set; }

    [JsonPropertyName("maskPiiByDefault")]
    public bool MaskPiiByDefault { get; set; }
}

public sealed class MigSurvivorRules
{
    [JsonPropertyName("preferNewerEmployment")]
    public bool PreferNewerEmployment { get; set; }

    [JsonPropertyName("preferCompleteNationalId")]
    public bool PreferCompleteNationalId { get; set; }

    [JsonPropertyName("forbidSilentFieldOverwrite")]
    public bool ForbidSilentFieldOverwrite { get; set; }
}

public sealed class MigFraudIndicator
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("forcesReview")]
    public bool ForcesReview { get; set; }
}

public sealed class MigMergeAuditPolicy
{
    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];

    [JsonPropertyName("reversibleRequired")]
    public bool ReversibleRequired { get; set; }

    [JsonPropertyName("gov003Join")]
    public string? Gov003Join { get; set; }
}

public sealed class MigReviewPrivacyPolicy
{
    [JsonPropertyName("tenantScopedReview")]
    public bool TenantScopedReview { get; set; }

    [JsonPropertyName("forbidCrossAgencyPii")]
    public bool ForbidCrossAgencyPii { get; set; }

    [JsonPropertyName("maskingMode")]
    public string? MaskingMode { get; set; }
}

public sealed class MigMatchSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("matchPolicyVersion")]
    public string? MatchPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class MigMatchBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
