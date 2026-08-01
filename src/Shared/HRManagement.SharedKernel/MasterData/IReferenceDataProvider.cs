namespace HRManagement.SharedKernel.MasterData;

/// <summary>MDM-001: abstraction for querying national reference catalog entries.</summary>
public interface IReferenceDataProvider
{
    Task<ReferenceCatalogEntry?> GetByCodeAsync(CatalogKind kind, string code, CancellationToken ct = default);

    Task<IReadOnlyList<ReferenceCatalogEntry>> ListActiveAsync(CatalogKind kind, CancellationToken ct = default);

    /// <summary>
    /// Resolves the effective entry for a given code at <paramref name="asOfDate"/>,
    /// following the <see cref="ReferenceCatalogEntry.ReplacedByCode"/> chain.
    /// </summary>
    Task<ReferenceCatalogEntry?> ResolveEffectiveAsync(CatalogKind kind, string code, DateTime asOfDate, CancellationToken ct = default);
}
