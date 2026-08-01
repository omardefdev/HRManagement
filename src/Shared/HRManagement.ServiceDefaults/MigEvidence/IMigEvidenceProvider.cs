namespace HRManagement.ServiceDefaults.MigEvidence;

public interface IMigEvidenceProvider
{
    MigEvidenceOptions Options { get; }
    MigEvidenceMatrixDocument Matrix { get; }
}

public interface IAgencyReconClaimGuard
{
    string? EnsureLiveAgencyReconAllowed();
    string? ValidateAccountedBreakdown(int migrated, int rejected, int deferred, int sourceTotal, bool rejectedReasonsPresent, bool deferredReasonsPresent);
    string? ValidateFinancialVariance(double unexplainedAmount);
    string? ValidateDualControl(IReadOnlyList<(string Role, string Actor)> signers);
    string? ValidateEvidenceImmutability(bool claimedImmutable, bool hashMatch);
    string? ValidateIndependentSqlClaim(bool liveIndependentSql);
    string? ValidateSigning(MigEvidenceSigning? meta);
}
