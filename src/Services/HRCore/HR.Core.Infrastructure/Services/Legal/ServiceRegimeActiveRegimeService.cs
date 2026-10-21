using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Legal;

/// <summary>SYR-008: active employment regime lookup for S2S callers.</summary>
public sealed class ServiceRegimeActiveRegimeService(HRCoreDbContext db)
{
    public async Task<IReadOnlyList<ActiveRegimeResult>> GetActiveRegimesAsync(
        IReadOnlyList<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];

        var distinct = employeeIds.Distinct().ToList();
        var records = await db.EmploymentRecords.AsNoTracking()
            .Where(r => distinct.Contains(r.EmployeeId) && r.EndDate == null)
            .OrderByDescending(r => r.StartDate)
            .ToListAsync(ct);

        var byEmployee = records
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        return distinct.Select(id =>
        {
            if (!byEmployee.TryGetValue(id, out var record))
                return new ActiveRegimeResult(id, null, null);

            return new ActiveRegimeResult(id, record.LegalRegimeId, record.Id);
        }).ToList();
    }
}

public sealed record ActiveRegimeResult(Guid EmployeeId, string? LegalRegimeId, Guid? EmploymentRecordId);
