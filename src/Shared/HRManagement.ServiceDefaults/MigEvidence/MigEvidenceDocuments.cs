using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.MigEvidence;

public sealed class MigEvidenceMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("evidencePolicyVersion")]
    public string? EvidencePolicyVersion { get; set; }

    [JsonPropertyName("accountedBuckets")]
    public List<MigAccountedBucket> AccountedBuckets { get; set; } = [];

    [JsonPropertyName("reconDimensions")]
    public List<string> ReconDimensions { get; set; } = [];

    [JsonPropertyName("financialPolicy")]
    public MigFinancialPolicy? FinancialPolicy { get; set; }

    [JsonPropertyName("dualControl")]
    public MigDualControlPolicy? DualControl { get; set; }

    [JsonPropertyName("evidencePackage")]
    public MigEvidencePackagePolicy? EvidencePackage { get; set; }

    [JsonPropertyName("sampling")]
    public MigSamplingPolicy? Sampling { get; set; }

    [JsonPropertyName("agencyWaveSignOff")]
    public MigAgencyWaveSignOffPolicy? AgencyWaveSignOff { get; set; }

    [JsonPropertyName("signing")]
    public MigEvidenceSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<MigEvidenceBlocker> Blockers { get; set; } = [];
}

public sealed class MigAccountedBucket
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("reasonRequired")]
    public bool ReasonRequired { get; set; }
}

public sealed class MigFinancialPolicy
{
    [JsonPropertyName("unexplainedVarianceForbidden")]
    public bool UnexplainedVarianceForbidden { get; set; }

    [JsonPropertyName("controlTotalTolerance")]
    public double ControlTotalTolerance { get; set; }

    [JsonPropertyName("payrollControlRequired")]
    public bool PayrollControlRequired { get; set; }
}

public sealed class MigDualControlPolicy
{
    [JsonPropertyName("minSigners")]
    public int MinSigners { get; set; }

    [JsonPropertyName("requireDistinctRoles")]
    public bool RequireDistinctRoles { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = [];
}

public sealed class MigEvidencePackagePolicy
{
    [JsonPropertyName("immutable")]
    public bool Immutable { get; set; }

    [JsonPropertyName("requiredArtifacts")]
    public List<string> RequiredArtifacts { get; set; } = [];

    [JsonPropertyName("contentHashAlg")]
    public string? ContentHashAlg { get; set; }

    [JsonPropertyName("wormJoin")]
    public string? WormJoin { get; set; }
}

public sealed class MigSamplingPolicy
{
    [JsonPropertyName("randomRate")]
    public double RandomRate { get; set; }

    [JsonPropertyName("statisticalMin")]
    public int StatisticalMin { get; set; }

    [JsonPropertyName("businessSignOffRequired")]
    public bool BusinessSignOffRequired { get; set; }
}

public sealed class MigAgencyWaveSignOffPolicy
{
    [JsonPropertyName("perWaveRequired")]
    public bool PerWaveRequired { get; set; }

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = [];
}

public sealed class MigEvidenceSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("evidencePolicyVersion")]
    public string? EvidencePolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class MigEvidenceBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
