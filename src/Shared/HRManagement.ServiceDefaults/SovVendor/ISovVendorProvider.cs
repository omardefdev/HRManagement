namespace HRManagement.ServiceDefaults.SovVendor;

public interface ISovVendorProvider
{
    SovVendorOptions Options { get; }
    SovVendorMatrixDocument Matrix { get; }
}

public interface IVendorClaimGuard
{
    string? EnsureLiveVendorAllowed();
    string? ValidateOwnership(bool ownedArtifactsPresent);
    string? ValidateOpenExport(bool fullExportPresent, bool formatsDocumented);
    string? ValidateCriticalAlternates(bool criticalWithoutAlternate);
    string? ValidateCleanRoom(bool ownedArtifactsOnly, bool alternateSitePresent);
    string? ValidateEscrow(bool escrowPresent, bool licenseRightsPresent);
    string? ValidateDestruction(bool attested);
    string? ValidateKnowledgeTransfer(bool transferPresent);
    string? ValidateExitAssistance(bool assistancePresent);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(SovVendorSigning? meta);
}
