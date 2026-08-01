namespace HRManagement.ServiceDefaults.SovThreat;

public interface ISovThreatProvider
{
    SovThreatOptions Options { get; }
    SovThreatMatrixDocument Matrix { get; }
}

public interface IThreatModelClaimGuard
{
    string? EnsureLiveThreatModelAllowed();
    string? ValidateMethodology(bool stridePresent);
    string? ValidateHighThreatCoverage(bool controlPresent, bool testPresent, bool ownerPresent);
    string? ValidateChangeTrigger(bool materialChangeCovered);
    string? ValidateMisuseLinkage(bool backlogLinked);
    string? ValidateResidualRisk(bool signed);
    string? ValidateCategoryCoverage(IEnumerable<string> missingCategories);
    string? ValidateTrustBoundaries(bool inventoryPresent);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(SovThreatSigning? meta);
}
