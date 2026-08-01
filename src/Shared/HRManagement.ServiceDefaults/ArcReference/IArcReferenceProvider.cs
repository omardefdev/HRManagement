namespace HRManagement.ServiceDefaults.ArcReference;

public interface IArcReferenceProvider
{
    ArcReferenceOptions Options { get; }
    ArcReferenceMatrixDocument Matrix { get; }
}

public interface IReferenceClaimGuard
{
    string? EnsureLiveReferenceAllowed();
    string? ValidateBoundedContexts(bool everyContextOwned, bool unjustifiedDistributionPresent);
    string? ValidateMultiSite(bool sameArtifactPrimaryAndDr, bool primaryAndDrProfilesPresent);
    string? ValidateTopology(bool unsupportedTopologyPresent, bool envelopeValidated);
    string? ValidateCoupling(bool directDbCrossBoundary, bool domainCouplingAcrossContexts);
    string? ValidateDataOwnership(bool singleWriterPresent);
    string? ValidateTenancy(bool tenantModelAligned);
    string? ValidatePlanes(bool planesSeparated);
    string? ValidateFitness(bool referenceImplementationPresent, bool labFitnessHarnessPresent);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ArcReferenceSigning? meta);
}
