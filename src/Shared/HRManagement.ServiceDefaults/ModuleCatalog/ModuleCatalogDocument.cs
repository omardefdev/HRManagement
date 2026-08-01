using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ModuleCatalog;

public sealed class ModuleCatalogDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("asOfDate")]
    public string? AsOfDate { get; set; }

    [JsonPropertyName("agencyVisibleTiers")]
    public List<string> AgencyVisibleTiers { get; set; } = [];

    [JsonPropertyName("allowedReadiness")]
    public List<string> AllowedReadiness { get; set; } = [];

    [JsonPropertyName("pilotApprovedSlugs")]
    public List<string> PilotApprovedSlugs { get; set; } = [];

    [JsonPropertyName("exceptions")]
    public List<ModuleCatalogException> Exceptions { get; set; } = [];

    [JsonPropertyName("modules")]
    public List<ModuleCatalogEntry> Modules { get; set; } = [];
}

public sealed class ModuleCatalogEntry
{
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("availability")]
    public string Availability { get; set; } = "";

    [JsonPropertyName("serviceTier")]
    public string ServiceTier { get; set; } = "";

    [JsonPropertyName("readinessStatus")]
    public string ReadinessStatus { get; set; } = "";

    [JsonPropertyName("uatPackRef")]
    public string? UatPackRef { get; set; }

    [JsonPropertyName("runbookRef")]
    public string? RunbookRef { get; set; }

    [JsonPropertyName("sloRef")]
    public string? SloRef { get; set; }

    [JsonPropertyName("opsCatalogId")]
    public string? OpsCatalogId { get; set; }

    [JsonPropertyName("ownerRole")]
    public string? OwnerRole { get; set; }
}

public sealed class ModuleCatalogException
{
    [JsonPropertyName("exceptionId")]
    public string ExceptionId { get; set; } = "";

    [JsonPropertyName("moduleId")]
    public string? ModuleId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
