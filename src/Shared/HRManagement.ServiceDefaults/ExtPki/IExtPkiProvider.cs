namespace HRManagement.ServiceDefaults.ExtPki;

public interface IExtPkiProvider
{
    ExtPkiOptions Options { get; }
    ExtPkiMatrixDocument Matrix { get; }
}

public interface IPkiSignatureClaimGuard
{
    string? EnsureLivePkiAllowed();
    string? ValidateSignerIntent(bool intentPresent, bool rolePresent);
    string? ValidateFormSignatureAsLegal(bool treatedAsLegal);
    string? ValidateOrgSeal(bool cryptoSealPresent, bool metadataOnly);
    string? ValidateRevocation(bool revoked, bool treatedAsValid);
    string? ValidateExpiredLtv(bool expired, bool hasLtv, bool treatedAsValid);
    string? ValidateClockSkew(int skewSeconds);
    string? ValidateKeyExfil(bool keyLeftHsm);
    string? ValidateAlgorithm(string alg);
    string? ValidateTsaOutage(bool tsaAvailable, bool treatedAsValid);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ExtPkiSigning? meta);
}
