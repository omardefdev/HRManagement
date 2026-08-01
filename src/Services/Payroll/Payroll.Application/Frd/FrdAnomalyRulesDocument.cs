using System.Text.Json.Serialization;

namespace Payroll.Application.Frd;

public sealed class FrdAnomalyRulesDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "FRD-001";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("detectionRules")]
    public List<FrdDetectionRule> DetectionRules { get; set; } = [];

    [JsonPropertyName("proofOfLife")]
    public FrdProofOfLifeSection? ProofOfLife { get; set; }

    [JsonPropertyName("paymentHold")]
    public FrdPaymentHoldSection? PaymentHold { get; set; }
}

public sealed class FrdDetectionRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "medium";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "flag-for-review";

    [JsonPropertyName("autoDetectable")]
    public bool AutoDetectable { get; set; } = true;
}

public sealed class FrdProofOfLifeSection
{
    [JsonPropertyName("holdOnExpiry")]
    public bool HoldOnExpiry { get; set; } = true;
}

public sealed class FrdPaymentHoldSection
{
    [JsonPropertyName("singleOperatorReleaseBlocked")]
    public bool SingleOperatorReleaseBlocked { get; set; } = true;
}
