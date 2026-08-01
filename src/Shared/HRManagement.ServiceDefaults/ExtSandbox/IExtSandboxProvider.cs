namespace HRManagement.ServiceDefaults.ExtSandbox;

public interface IExtSandboxProvider
{
    ExtSandboxOptions Options { get; }
    ExtSandboxMatrixDocument Matrix { get; }
}

public interface ISandboxCertClaimGuard
{
    string? EnsureLivePortalAllowed();
    string? ValidateNoProdAccess(bool attemptedProd);
    string? ValidateNoRealPii(bool containsRealPii);
    string? ValidateEphemeralCredential(bool isEphemeral, bool isManualSecret);
    string? ValidateIncompatibleClient(bool treatedAsCertified, bool suitePassed);
    string? ValidateDocsMatch(bool docsMatchContract);
    string? ValidateRevocation(bool compromised, bool revoked);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ExtSandboxSigning? meta);
}
