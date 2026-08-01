using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ProgramGovernance;

public sealed class GovernanceMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("asOfDate")]
    public string? AsOfDate { get; set; }

    [JsonPropertyName("criticalDecisionTypes")]
    public List<string> CriticalDecisionTypes { get; set; } = [];

    [JsonPropertyName("goLiveMultiSeat")]
    public GoLiveMultiSeatRules? GoLiveMultiSeat { get; set; }

    [JsonPropertyName("exceptions")]
    public List<GovernanceException> Exceptions { get; set; } = [];

    [JsonPropertyName("blockers")]
    public List<GovernanceBlocker> Blockers { get; set; } = [];
}

public sealed class GoLiveMultiSeatRules
{
    [JsonPropertyName("forbidden")]
    public List<string> Forbidden { get; set; } = [];

    [JsonPropertyName("requiredSeats")]
    public List<string> RequiredSeats { get; set; } = [];

    [JsonPropertyName("optionalSeatsWhenDataImpact")]
    public List<string> OptionalSeatsWhenDataImpact { get; set; } = [];

    [JsonPropertyName("engRoleAliases")]
    public List<string> EngRoleAliases { get; set; } = [];
}

public sealed class GovernanceException
{
    [JsonPropertyName("exceptionId")]
    public string ExceptionId { get; set; } = "";

    [JsonPropertyName("declaredBy")]
    public string? DeclaredBy { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("expiresOn")]
    public string? ExpiresOn { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public sealed class GovernanceBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed class DecisionLogDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("decisions")]
    public List<GovernanceDecision> Decisions { get; set; } = [];
}

public sealed class GovernanceDecision
{
    [JsonPropertyName("decisionId")]
    public string DecisionId { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("ownerRole")]
    public string? OwnerRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("board")]
    public string? Board { get; set; }

    [JsonPropertyName("adrRef")]
    public string? AdrRef { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("evidenceRef")]
    public string? EvidenceRef { get; set; }

    [JsonPropertyName("decidedAt")]
    public string? DecidedAt { get; set; }
}

public sealed class RiskRegisterDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("risks")]
    public List<GovernanceRisk> Risks { get; set; } = [];
}

public sealed class GovernanceRisk
{
    [JsonPropertyName("riskId")]
    public string RiskId { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("linkedDecisionId")]
    public string? LinkedDecisionId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class GoLiveEvidenceDocument
{
    [JsonPropertyName("fixtureId")]
    public string? FixtureId { get; set; }

    [JsonPropertyName("expectedValidate")]
    public string? ExpectedValidate { get; set; }

    [JsonPropertyName("seats")]
    public List<string> Seats { get; set; } = [];

    [JsonPropertyName("declaredByRole")]
    public string? DeclaredByRole { get; set; }

    [JsonPropertyName("optionalDataSeat")]
    public bool OptionalDataSeat { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}
