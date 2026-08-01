namespace HRManagement.ServiceDefaults.MigFactory;

public interface IMigFactoryProvider
{
    MigFactoryOptions Options { get; }
    MigFactoryMatrixDocument Matrix { get; }
}

public interface IEtlWorkersClaimGuard
{
    string? EnsureLiveEtlWorkersAllowed();
    string? ValidateIdempotency(MigIdempotencyPolicy? policy);
    string? ValidateStagingZone(MigStagingZone zone);
    string? ValidateLineageFields(IReadOnlyList<string>? fields);
    string? ValidatePiiLogSample(IEnumerable<string> sampleKeys);
    string? ValidateThroughputClaim(bool liveProof);
}
