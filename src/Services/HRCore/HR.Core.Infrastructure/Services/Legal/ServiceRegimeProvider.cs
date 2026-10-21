using System.Linq.Expressions;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Legal;

/// <summary>SYR-003: EF read provider for service regimes.</summary>
public sealed class ServiceRegimeProvider(HRCoreDbContext db) : IServiceRegimeProvider
{
    public async Task<ServiceRegimeSummaryDto?> GetByIdAsync(string legalRegimeId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(legalRegimeId))
            return null;

        return await db.LegalRegimes
            .AsNoTracking()
            .Where(r => r.LegalRegimeId == legalRegimeId)
            .Select(MapSummary)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ServiceRegimeSummaryDto>> ListActiveAsync(CancellationToken ct = default) =>
        await db.LegalRegimes
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.RegimeName)
            .Select(MapSummary)
            .ToListAsync(ct);

    public async Task<ServiceRegimeSummaryDto?> ResolveByLegalSourceIdAsync(string legalSourceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(legalSourceId))
            return null;

        return await db.LegalRegimes
            .AsNoTracking()
            .Where(r => r.ResolvedLegalSourceId == legalSourceId && r.IsActive)
            .Select(MapSummary)
            .FirstOrDefaultAsync(ct);
    }

    private static readonly Expression<Func<LegalRegime, ServiceRegimeSummaryDto>> MapSummary =
        r => new ServiceRegimeSummaryDto(
            r.LegalRegimeId,
            r.RegimeName,
            r.Type,
            r.LegislationRef,
            r.ResolvedLegalSourceId,
            r.EffectiveFrom,
            r.EffectiveTo,
            r.IsActive);
}
