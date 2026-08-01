namespace HRManagement.ServiceDefaults.SovData;

public interface ISovDataProvider
{
    SovDataOptions Options { get; }
    SovDataMatrixDocument Matrix { get; }
}

public interface ISovereigntyClaimGuard
{
    string? EnsureLiveSovereigntyAllowed();
    string? ValidateCopyClassification(bool classified, bool approvedLocation);
    string? ValidateResidency(bool insideApprovedRegion);
    string? ValidateEgress(bool denyByDefault);
    string? ValidateRemoteSupport(bool jit, bool logged, bool approved);
    string? ValidateDestruction(bool certificatePresent);
    string? ValidateLawfulDisclosure(bool logged);
    string? ValidateDataFlowMap(bool mapPresent);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(SovDataSigning? meta);
}
