namespace HRManagement.ServiceDefaults.ProdBranch;

public interface IProdBranchProvider
{
    ProdBranchOptions Options { get; }
    ProdBranchMatrixDocument Matrix { get; }
}

public interface IRemoteBranchClaimGuard
{
    string? EnsureLiveRemoteBranchAllowed();
    string? ValidatePerformanceBudget(bool withinBudget);
    string? ValidateNoDataLossOnInterrupt(bool interrupted, bool dataPreserved);
    string? ValidateResumableUpload(bool resumable);
    string? ValidateDraftSaved(bool draftPresent);
    string? ValidateIdempotentRetry(bool idempotent);
    string? ValidateLocalizationReady(bool arabicEnglish, bool rtlLtr);
    string? ValidateOfflinePolicy(bool encrypted, bool limited, bool threatModelPresent);
    string? ValidateWcagRequired(bool wcag22Aa);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ProdBranchSigning? meta);
}
