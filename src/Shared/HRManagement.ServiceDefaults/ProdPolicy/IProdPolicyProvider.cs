namespace HRManagement.ServiceDefaults.ProdPolicy;

public interface IProdPolicyProvider
{
    ProdPolicyOptions Options { get; }
    ProdPolicyMatrixDocument Matrix { get; }
}

public interface IPolicyPlatformClaimGuard
{
    string? EnsureLiveEngineAllowed();
    string? ValidateVersionedTemplate(bool hasVersion, string? kind);
    string? ValidateHistoricalPin(string? asOf, bool resolvedVersionMatchesPin);
    string? ValidateFourEyesPublish(string? publisherRole, string? reviewerRole);
    string? ValidateSimulationSynthetic(bool usesRealData);
    string? ValidateInvalidPublish(bool isValid, bool published);
    string? ValidateOverrideCeiling(bool overrideTouchesNationalMandatory);
    string? ValidateNoArbitraryCode(bool containsExecutableCode);
    string? ValidateRollback(bool hasRollbackRef);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ProdPolicySigning? meta);
}
