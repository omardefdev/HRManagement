namespace HRManagement.ServiceDefaults.ExtApi;

public interface IExtApiProvider
{
    ExtApiOptions Options { get; }
    ExtApiMatrixDocument Matrix { get; }
}

public interface IApiExchangeClaimGuard
{
    string? EnsureLiveExchangeAllowed();
    string? ValidateCatalogEntry(IEnumerable<string> presentFields);
    string? ValidateBreakingChange(bool isBreaking, bool majorBumped);
    string? ValidateSharedDb(bool usesSharedDb);
    string? ValidatePurposeExport(string purpose, bool consentPresent);
    string? ValidateCertifiedConsumerBreak(bool breaksCertified);
    string? ValidateSchemaCompat(bool compatible);
    string? ValidateDeprecationWindow(int noticeDays);
    string? ValidateThrottleExceed(bool exceeded, bool returned429);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ExtApiSigning? meta);
}
