namespace HRManagement.ServiceDefaults.ProdExtensibility;

public interface IProdExtensibilityProvider
{
    ProdExtensibilityOptions Options { get; }
    ProdExtensibilityMatrixDocument Matrix { get; }
}

public interface IExtensibilityClaimGuard
{
    string? EnsureLiveExtensibilityAllowed();
    string? ValidateN2Compatibility(bool withinWindow);
    string? ValidatePluginIsolation(bool withinTenant, bool coreHealthy);
    string? ValidateQuota(bool withinLimits);
    string? ValidateDeprecation(bool breakingChange, bool noticeSatisfied);
    string? ValidateNoDirectDb(bool directDbAttempted);
    string? ValidateNoCustomFork(bool customForkAttempted);
    string? ValidateUpgradePath(bool rehearsalPresent);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ProdExtensibilitySigning? meta);
}
