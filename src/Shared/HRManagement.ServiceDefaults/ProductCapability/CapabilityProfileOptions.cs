namespace HRManagement.ServiceDefaults.ProductCapability;

public sealed class CapabilityProfileOptions
{
    public const string SectionName = "CapabilityProfile";

    /// <summary>Default agency archetype when header/claim is absent.</summary>
    public string? Archetype { get; set; }

    /// <summary>This service's module id (e.g. hr-core, payroll).</summary>
    public string? ModuleId { get; set; }

    /// <summary>Override path to capability-map.json.</summary>
    public string? CapabilityMapPath { get; set; }

    /// <summary>Override path to agency-archetypes.json.</summary>
    public string? ArchetypesPath { get; set; }

    /// <summary>When true, middleware returns 403 for disabled modules.</summary>
    public bool EnforceModuleGuard { get; set; } = true;
}
