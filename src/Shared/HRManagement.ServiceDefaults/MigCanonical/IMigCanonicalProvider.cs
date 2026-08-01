namespace HRManagement.ServiceDefaults.MigCanonical;

public interface IMigCanonicalProvider
{
    MigCanonicalOptions Options { get; }
    MigCanonicalMatrixDocument Matrix { get; }
}

public interface ICodeMapClaimGuard
{
    string? EnsureLiveCodeMapEngineAllowed();
    string? ValidateDisposition(MigCodeMap map);
    string? ValidateLegacyIdUse(string proposedKey);
    string? ValidateSigning(MigCodeMapSigning? meta);
}
