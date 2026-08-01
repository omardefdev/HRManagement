namespace HRManagement.ServiceDefaults.ProdAgency;

public interface IProdAgencyProvider
{
    ProdAgencyOptions Options { get; }
    ProdAgencyMatrixDocument Matrix { get; }
}

public interface IAgencySelfAdminClaimGuard
{
    string? EnsureLiveSelfAdminAllowed();
    string? ValidateDelegatedScope(bool scopeAllowed, bool noDbaPath);
    string? ValidateNationalControlsImmutable(bool attemptedNationalMutate);
    string? ValidateCrossTenantForbidden(bool targetTenantEqualsActorTenant);
    string? ValidateFourEyesSensitive(bool isSensitive, string? publisherRole, string? reviewerRole);
    string? ValidateRollback(bool hasRollbackRef);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidatePreview(bool previewUsed);
    string? ValidateSigning(ProdAgencySigning? meta);
}
