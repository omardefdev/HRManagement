namespace HRManagement.ServiceDefaults.SovZt;

public interface ISovZtProvider
{
    SovZtOptions Options { get; }
    SovZtMatrixDocument Matrix { get; }
}

public interface IPamClaimGuard
{
    string? EnsureLivePamAllowed();
    string? ValidateNoStandingPrivilege(bool hasStanding, bool sharedRoot);
    string? ValidateJit(bool temporary, bool reasonPresent, bool approved);
    string? ValidateAdminWorkstation(bool fromOrdinaryDevice);
    string? ValidateBreakGlass(bool alerted, bool reviewed);
    string? ValidateMfa(bool phishingResistant);
    string? ValidateRecertification(bool dormantOrphanPresent);
    string? ValidateSessionRecord(bool recorded);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(SovZtSigning? meta);
}
