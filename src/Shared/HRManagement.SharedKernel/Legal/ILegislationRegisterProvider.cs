namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-001: read-only access to the national legislation register (EF-backed).</summary>
public interface ILegislationRegisterProvider
{
    Task<LegislationSourceSummaryDto?> GetByIdAsync(string legalSourceId, CancellationToken ct = default);

    Task<LegislationSourceSummaryDto?> ResolveAliasAsync(string alias, CancellationToken ct = default);

    Task<IReadOnlyList<LegislationSourceSummaryDto>> ListActiveAsync(CancellationToken ct = default);

    Task<LegislationSourceDetailDto?> GetDetailAsync(string legalSourceId, CancellationToken ct = default);

    Task<IReadOnlyList<LegislationKeyArticleDto>> GetKeyArticlesAsync(string legalSourceId, CancellationToken ct = default);

    Task<bool> ExistsAsync(string legalSourceId, CancellationToken ct = default);
}
