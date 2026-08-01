namespace HRManagement.ServiceDefaults.ProdWorkbox;

public interface IProdWorkboxProvider
{
    ProdWorkboxOptions Options { get; }
    ProdWorkboxMatrixDocument Matrix { get; }
}

public interface IWorkboxClaimGuard
{
    string? EnsureLiveWorkboxAllowed();
    string? ValidateNoLostTransaction(bool persistedAcrossModules);
    string? ValidateSafeSearch(bool withinTenant, bool withinOrg, bool existenceLeakAttempted);
    string? ValidateBulkApprove(bool withinLimits, bool fourEyesSatisfied);
    string? ValidateNotificationSafe(bool containsSecret, bool hasDeliveryReceipt, bool leadsToCorrectState);
    string? ValidateDeepLink(bool expired, bool reauthPerformed);
    string? ValidateEscalationTracked(bool escalationPresent);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ProdWorkboxSigning? meta);
}
