using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.LegalControl;

public sealed class ControlCatalogDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("asOfDate")]
    public string? AsOfDate { get; set; }

    [JsonPropertyName("allowedDomains")]
    public List<string> AllowedDomains { get; set; } = [];

    [JsonPropertyName("allowedStatuses")]
    public List<string> AllowedStatuses { get; set; } = [];

    [JsonPropertyName("exceptions")]
    public List<CatalogException> Exceptions { get; set; } = [];

    [JsonPropertyName("controls")]
    public List<CatalogControl> Controls { get; set; } = [];

    [JsonPropertyName("blockers")]
    public List<CatalogBlocker> Blockers { get; set; } = [];
}

public sealed class CatalogControl
{
    [JsonPropertyName("requirementId")]
    public string RequirementId { get; set; } = "";

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("sourceRef")]
    public string? SourceRef { get; set; }

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "";

    [JsonPropertyName("cardIds")]
    public List<string> CardIds { get; set; } = [];

    [JsonPropertyName("controlOwner")]
    public string? ControlOwner { get; set; }

    [JsonPropertyName("evidencePath")]
    public string? EvidencePath { get; set; }

    [JsonPropertyName("testRef")]
    public string? TestRef { get; set; }

    [JsonPropertyName("frequency")]
    public string? Frequency { get; set; }

    [JsonPropertyName("exceptionId")]
    public string? ExceptionId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

public sealed class CatalogException
{
    [JsonPropertyName("exceptionId")]
    public string ExceptionId { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("expiresOn")]
    public string? ExpiresOn { get; set; }

    [JsonPropertyName("linkedRequirementIds")]
    public List<string> LinkedRequirementIds { get; set; } = [];

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed class CatalogBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed class LegislativeWatchStamp
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "STR-002";

    [JsonPropertyName("lastReviewed")]
    public string LastReviewed { get; set; } = "";

    [JsonPropertyName("nextDue")]
    public string NextDue { get; set; } = "";

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
