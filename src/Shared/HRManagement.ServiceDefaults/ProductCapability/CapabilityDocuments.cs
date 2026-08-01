namespace HRManagement.ServiceDefaults.ProductCapability;

public sealed class CapabilityMapDocument
{
    public string Card { get; set; } = "";
    public int Version { get; set; }
    public string AsOfDate { get; set; } = "";
    public List<CapabilityDomainEntry> CapabilityDomains { get; set; } = [];
    public List<CapabilityServiceEntry> Services { get; set; } = [];
}

public sealed class CapabilityDomainEntry
{
    public string Id { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";
}

public sealed class CapabilityServiceEntry
{
    public string ModuleId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string CapabilityDomain { get; set; } = "";
    public string OwnerRole { get; set; } = "";
    public bool UatRequired { get; set; }
    public string SloRef { get; set; } = "";
    public Dictionary<string, string> ArchetypeClassification { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AgencyArchetypesDocument
{
    public string Card { get; set; } = "";
    public int Version { get; set; }
    public string AsOfDate { get; set; } = "";
    public List<string> AllowedEntitlements { get; set; } = [];
    public List<string> RequiredModuleIds { get; set; } = [];
    public List<AgencyArchetypeEntry> Archetypes { get; set; } = [];
}

public sealed class AgencyArchetypeEntry
{
    public string Id { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string Description { get; set; } = "";
    public string ScaleTier { get; set; } = "";
    public Dictionary<string, string> ModuleEntitlements { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
