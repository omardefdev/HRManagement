using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.Ownership;

public sealed class OwnershipMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("asOfDate")]
    public string? AsOfDate { get; set; }

    [JsonPropertyName("requiredVendorClauses")]
    public List<string> RequiredVendorClauses { get; set; } = [];

    [JsonPropertyName("busFactorRule")]
    public BusFactorRule? BusFactorRule { get; set; }

    [JsonPropertyName("budgetRule")]
    public BudgetRule? BudgetRule { get; set; }

    [JsonPropertyName("components")]
    public List<OwnershipComponent> Components { get; set; } = [];

    [JsonPropertyName("staffing")]
    public OwnershipStaffing? Staffing { get; set; }

    [JsonPropertyName("budgetLines")]
    public List<OwnershipBudgetLine> BudgetLines { get; set; } = [];

    [JsonPropertyName("vendors")]
    public List<OwnershipVendor> Vendors { get; set; } = [];

    [JsonPropertyName("blockers")]
    public List<OwnershipBlocker> Blockers { get; set; } = [];

    [JsonPropertyName("tcoApproval")]
    public OwnershipTcoApproval? TcoApproval { get; set; }
}

public sealed class BusFactorRule
{
    [JsonPropertyName("criticalMinBusFactor")]
    public int CriticalMinBusFactor { get; set; } = 2;

    [JsonPropertyName("tier0MinBackups")]
    public int Tier0MinBackups { get; set; } = 1;
}

public sealed class BudgetRule
{
    [JsonPropertyName("mustFundCategories")]
    public List<string> MustFundCategories { get; set; } = [];

    [JsonPropertyName("forbidDevOnly")]
    public bool ForbidDevOnly { get; set; } = true;

    [JsonPropertyName("horizonYears")]
    public int HorizonYears { get; set; } = 5;
}

public sealed class OwnershipComponent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "";

    [JsonPropertyName("critical")]
    public bool Critical { get; set; }

    [JsonPropertyName("productOwnerRole")]
    public string? ProductOwnerRole { get; set; }

    [JsonPropertyName("serviceOwnerRole")]
    public string? ServiceOwnerRole { get; set; }

    [JsonPropertyName("dataOwnerRole")]
    public string? DataOwnerRole { get; set; }

    [JsonPropertyName("securityOwnerRole")]
    public string? SecurityOwnerRole { get; set; }

    [JsonPropertyName("primaryRole")]
    public string? PrimaryRole { get; set; }

    [JsonPropertyName("backupRoles")]
    public List<string> BackupRoles { get; set; } = [];

    [JsonPropertyName("busFactor")]
    public int BusFactor { get; set; }
}

public sealed class OwnershipStaffing
{
    [JsonPropertyName("national24x7Claim")]
    public bool National24x7Claim { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

public sealed class OwnershipBudgetLine
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("funded")]
    public bool Funded { get; set; }

    [JsonPropertyName("ownerRole")]
    public string? OwnerRole { get; set; }

    [JsonPropertyName("years")]
    public List<int> Years { get; set; } = [];
}

public sealed class OwnershipVendor
{
    [JsonPropertyName("vendorId")]
    public string VendorId { get; set; } = "";

    [JsonPropertyName("componentIds")]
    public List<string> ComponentIds { get; set; } = [];

    [JsonPropertyName("rtoHours")]
    public int? RtoHours { get; set; }

    [JsonPropertyName("incidentDisclosureHours")]
    public int? IncidentDisclosureHours { get; set; }

    [JsonPropertyName("exitAssistance")]
    public bool ExitAssistance { get; set; }

    [JsonPropertyName("clauses")]
    public List<string> Clauses { get; set; } = [];
}

public sealed class OwnershipBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

public sealed class OwnershipTcoApproval
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("independentFinanceReview")]
    public bool IndependentFinanceReview { get; set; }
}
