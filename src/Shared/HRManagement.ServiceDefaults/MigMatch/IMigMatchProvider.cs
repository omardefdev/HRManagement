namespace HRManagement.ServiceDefaults.MigMatch;

public interface IMigMatchProvider
{
    MigMatchOptions Options { get; }
    MigMatchMatrixDocument Matrix { get; }
}

public interface IIdentityMatchClaimGuard
{
    string? EnsureLiveIdentityMatchAllowed();
    string? ValidateProposedOutcome(string? outcome, string? risk, string? evidenceRef);
    string? ValidateMergeAudit(IReadOnlyDictionary<string, string?> fields, bool reversible);
    string? ValidateReviewPrivacy(bool crossTenant);
    string? ValidateSigning(MigMatchSigning? meta);
    string? ValidateLiveMetricsClaim(bool liveAgencyEval);
}
