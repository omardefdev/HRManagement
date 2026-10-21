namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-003: read-only access to service regimes (EF-backed).</summary>
public interface IServiceRegimeProvider
{
    Task<ServiceRegimeSummaryDto?> GetByIdAsync(string legalRegimeId, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceRegimeSummaryDto>> ListActiveAsync(CancellationToken ct = default);

    Task<ServiceRegimeSummaryDto?> ResolveByLegalSourceIdAsync(string legalSourceId, CancellationToken ct = default);
}
