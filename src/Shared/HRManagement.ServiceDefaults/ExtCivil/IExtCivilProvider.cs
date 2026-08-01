namespace HRManagement.ServiceDefaults.ExtCivil;

public interface IExtCivilProvider
{
    ExtCivilOptions Options { get; }
    ExtCivilMatrixDocument Matrix { get; }
}

public interface ICivilRegistryClaimGuard
{
    string? EnsureLiveMoiAllowed();
    string? ValidatePurpose(string purposeCode);
    string? ValidateConsent(bool consentPresent);
    string? ValidateStoredFields(IEnumerable<string> fields);
    string? ValidateOutageAsTruth(bool registryAvailable, bool treatedAsVerified);
    string? ValidateStaleAsTruth(bool stale, bool usedAsTruth);
    string? ValidateBulkHarvest(bool attempted);
    string? ValidateMismatchWorkflow(bool isMismatch, bool humanQueued);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ExtCivilSigning? meta);
}
