using HR.Core.Infrastructure.Services.OrgStructure;

namespace HR.Core.Infrastructure.Services.Establishment;

public sealed class EstablishmentAsOfService(
    IJobCatalogVersionService jobCatalogVersions,
    IFundedPositionVersionService fundedPositionVersions)
{
    public Task<JobCatalogVersionDto?> ResolveCatalogAsOfAsync(Guid jobCatalogEntryId, DateTime asOf, CancellationToken ct = default) =>
        jobCatalogVersions.ResolveAsOfAsync(jobCatalogEntryId, asOf, ct);

    public Task<FundedPositionVersionDto?> ResolvePositionAsOfAsync(Guid fundedPositionId, DateTime asOf, CancellationToken ct = default) =>
        fundedPositionVersions.ResolveAsOfAsync(fundedPositionId, asOf, ct);

    public Task<IReadOnlyList<FundedPositionAsOfSnapshot>> ListUnitPositionsAsOfAsync(
        Guid orgUnitId,
        DateTime asOf,
        CancellationToken ct = default) =>
        fundedPositionVersions.ListPositionsAsOfAsync(orgUnitId, asOf, ct);

    public async Task<int> BackfillAllMissingVersionsAsync(CancellationToken ct = default)
    {
        var catalog = await jobCatalogVersions.BackfillMissingVersionsAsync(ct);
        var positions = await fundedPositionVersions.BackfillMissingVersionsAsync(ct);
        return catalog + positions;
    }
}
