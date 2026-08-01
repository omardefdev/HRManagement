namespace HRManagement.ServiceDefaults.ModuleCatalog;

public interface IModuleCatalogProvider
{
    ModuleCatalogOptions Options { get; }
    ModuleCatalogDocument Catalog { get; }

    ModuleCatalogEntry? GetModule(string moduleId);
    IReadOnlyList<ModuleCatalogEntry> GetAgencyVisibleModules();
    IReadOnlyList<string> PilotApprovedSlugs { get; }
    bool HasActiveException(string moduleId);
    string? ResolveRepoRoot();
}
