using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ValueKpi;

public sealed class ValueKpiMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("requiredKpiFields")]
    public List<string> RequiredKpiFields { get; set; } = [];

    [JsonPropertyName("forbiddenBenefitMeasures")]
    public List<string> ForbiddenBenefitMeasures { get; set; } = [];

    [JsonPropertyName("privacyRules")]
    public ValuePrivacyRules? PrivacyRules { get; set; }

    [JsonPropertyName("kpis")]
    public List<ValueKpiDefinition> Kpis { get; set; } = [];

    [JsonPropertyName("baselines")]
    public List<ValueKpiBaseline> Baselines { get; set; } = [];

    [JsonPropertyName("blockers")]
    public List<ValueKpiBlocker> Blockers { get; set; } = [];
}

public sealed class ValuePrivacyRules
{
    [JsonPropertyName("tenantScoped")]
    public bool TenantScoped { get; set; }

    [JsonPropertyName("minCellSize")]
    public int MinCellSize { get; set; }

    [JsonPropertyName("forbidCrossTenantRollupWithoutWaiver")]
    public bool ForbidCrossTenantRollupWithoutWaiver { get; set; }
}

public sealed class ValueKpiDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("definition")]
    public string? Definition { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("formula")]
    public string? Formula { get; set; }

    [JsonPropertyName("sourceRef")]
    public string? SourceRef { get; set; }

    [JsonPropertyName("ownerRole")]
    public string? OwnerRole { get; set; }

    [JsonPropertyName("auditorRole")]
    public string? AuditorRole { get; set; }

    [JsonPropertyName("target")]
    public object? Target { get; set; }

    [JsonPropertyName("baselineRequired")]
    public bool BaselineRequired { get; set; }

    [JsonPropertyName("antiGamingRules")]
    public List<string> AntiGamingRules { get; set; } = [];

    [JsonPropertyName("benefitCategory")]
    public string? BenefitCategory { get; set; }

    [JsonPropertyName("moduleIds")]
    public List<string> ModuleIds { get; set; } = [];
}

public sealed class ValueKpiBaseline
{
    [JsonPropertyName("kpiId")]
    public string KpiId { get; set; } = "";

    [JsonPropertyName("prePilotFreeze")]
    public bool PrePilotFreeze { get; set; }

    [JsonPropertyName("asOfDate")]
    public string? AsOfDate { get; set; }

    [JsonPropertyName("legacySystem")]
    public string? LegacySystem { get; set; }
}

public sealed class ValueKpiBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
