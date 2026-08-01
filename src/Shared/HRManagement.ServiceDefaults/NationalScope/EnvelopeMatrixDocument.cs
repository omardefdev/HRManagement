using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.NationalScope;

public sealed class EnvelopeMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("asOfDate")]
    public string? AsOfDate { get; set; }

    [JsonPropertyName("allowedEnvelopes")]
    public List<string> AllowedEnvelopes { get; set; } = [];

    [JsonPropertyName("indicators")]
    public List<EnvelopeIndicator> Indicators { get; set; } = [];

    [JsonPropertyName("blockers")]
    public List<EnvelopeBlocker> Blockers { get; set; } = [];
}

public sealed class EnvelopeIndicator
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("ownerRole")]
    public string? OwnerRole { get; set; }

    [JsonPropertyName("asOfDate")]
    public string? AsOfDate { get; set; }

    [JsonPropertyName("sourceTier")]
    public string? SourceTier { get; set; }

    [JsonPropertyName("envelope")]
    public string Envelope { get; set; } = "";

    [JsonPropertyName("assumptionId")]
    public string? AssumptionId { get; set; }

    public decimal? AsDecimal()
    {
        if (Value is null) return null;
        if (Value is System.Text.Json.JsonElement el)
        {
            if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetDecimal(out var d))
                return d;
            if (el.ValueKind == System.Text.Json.JsonValueKind.String
                && decimal.TryParse(el.GetString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var ds))
                return ds;
            return null;
        }

        return Convert.ToDecimal(Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public string? AsString()
    {
        if (Value is null) return null;
        if (Value is System.Text.Json.JsonElement el)
            return el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() : el.ToString();
        return Convert.ToString(Value, System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class EnvelopeBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
