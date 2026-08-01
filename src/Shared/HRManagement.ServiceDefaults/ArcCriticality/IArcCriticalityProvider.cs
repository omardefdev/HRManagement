namespace HRManagement.ServiceDefaults.ArcCriticality;

public interface IArcCriticalityProvider
{
    ArcCriticalityOptions Options { get; }
    ArcCriticalityMatrixDocument Matrix { get; }
}

public interface ICriticalityClaimGuard
{
    string? EnsureLiveCriticalityAllowed();
    string? ValidateClassification(bool allServicesClassified, bool allDependenciesClassified);
    string? ValidateJourneyMap(bool criticalJourneysMapped);
    string? ValidateSharedPlatform(IEnumerable<string> missingSurfaces);
    string? ValidateTelemetryBinding(bool targetWithoutTelemetry);
    string? ValidateSpof(bool unacceptableSpofInTier01);
    string? ValidateRecoveryOrder(bool orderedStepsPresent, bool drillEvidencePresent);
    string? ValidateFailureDomains(bool domainsDocumented);
    string? ValidateGracefulDegradation(bool tier01Documented);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ArcCriticalitySigning? meta);
}
