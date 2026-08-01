namespace HRManagement.ServiceDefaults.MigCutover;

public interface IMigCutoverProvider
{
    MigCutoverOptions Options { get; }
    MigCutoverMatrixDocument Matrix { get; }
}

public interface IAgencyCutoverClaimGuard
{
    string? EnsureLiveAgencyCutoverAllowed();
    string? ValidateRehearsal(MigCutoverRehearsal rehearsal);
    string? ValidateDelta(int lostCount);
    string? ValidateRollback(bool exercised, bool paperOnly);
    string? ValidateGoNoGo(IReadOnlyDictionary<string, bool> criteriaMet);
    string? ValidateLegacyAccess(bool readOnly, bool secured);
    string? ValidateLiveDnsClaim(bool liveDns);
    string? ValidateSigning(MigCutoverSigning? meta);
}
