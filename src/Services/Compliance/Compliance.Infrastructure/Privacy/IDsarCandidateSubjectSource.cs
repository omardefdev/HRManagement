namespace Compliance.Infrastructure.Privacy;

public interface IDsarCandidateSubjectSource
{
    string SourceName { get; }

    Task<(bool Collected, string? Error, IReadOnlyList<DsarDiscoveryField> Fields)> FetchAsync(
        Guid tenantId,
        Guid? citizenApplicationId,
        string? nationalIdHash,
        CancellationToken cancellationToken);
}
