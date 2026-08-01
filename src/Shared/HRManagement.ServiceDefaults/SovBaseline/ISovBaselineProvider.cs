namespace HRManagement.ServiceDefaults.SovBaseline;

public interface ISovBaselineProvider
{
    SovBaselineOptions Options { get; }
    SovBaselineMatrixDocument Matrix { get; }
}

public interface IAtoClaimGuard
{
    string? EnsureLiveAtoAllowed();
    string? ValidateControlOwnership(bool hasOwner, bool hasEvidence);
    string? ValidateCoverage(bool statusKnown, bool siteReasonPresentIfNa);
    string? ValidatePoam(bool datedRemediationPresent);
    string? ValidateException(bool signed);
    string? ValidateAuthorizationBoundary(bool boundaryPresent);
    string? ValidateReassessment(bool triggeredAfterMaterialChange);
    string? ValidateIndependentAssessment(bool samplePresent);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(SovBaselineSigning? meta);
}
