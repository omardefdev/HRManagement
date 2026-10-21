using Compliance.Domain.Entities;

namespace Compliance.Infrastructure.Privacy;

public interface IDsarDataCollectorService
{
    Task<DsarDiscoveryPackage> CollectAsync(
        DsarRequest request,
        Guid exportedByUserId,
        Guid? requesterUserId,
        CancellationToken cancellationToken = default);
}

public interface IDsarSubjectSource
{
    string SourceName { get; }

    Task<(bool Collected, string? Error, IReadOnlyList<DsarDiscoveryField> Fields)> FetchAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken cancellationToken);
}
