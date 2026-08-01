namespace HRManagement.ServiceDefaults.LegalControl;

public interface ILegalControlCatalogProvider
{
    LegalControlOptions Options { get; }
    ControlCatalogDocument Catalog { get; }
    LegislativeWatchStamp? WatchStamp { get; }
    string ReleaseProfile { get; }

    CatalogControl? GetControl(string requirementId);
    IReadOnlyList<CatalogControl> GetControlsByDomain(string domain);
    CatalogException? GetException(string exceptionId);
    bool IsExceptionActive(string? exceptionId);
}
