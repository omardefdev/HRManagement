namespace HRManagement.ServiceDefaults.ModuleCatalog;

public sealed class ModuleCatalogOptions
{
    public const string SectionName = "ModuleCatalog";

    /// <summary>When true, invalid EnabledModules / readiness fail host startup.</summary>
    public bool EnforceOnStartup { get; set; }

    /// <summary>When true, path middleware denies non-entitled module segments.</summary>
    public bool EnforcePathEntitlement { get; set; }

    /// <summary>Allow exposing NotReady modules (lab waiver only).</summary>
    public bool AllowNotReadyExposure { get; set; }

    /// <summary>When true and a repo root is resolvable, Ready evidence files must exist on disk.</summary>
    public bool ValidateEvidenceFilesOnDisk { get; set; }

    /// <summary>Optional absolute/relative path to module-catalog.json.</summary>
    public string? CatalogPath { get; set; }
}
