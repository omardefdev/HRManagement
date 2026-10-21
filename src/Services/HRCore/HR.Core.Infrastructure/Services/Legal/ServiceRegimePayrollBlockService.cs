using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Legal;

/// <summary>SYR-007: payroll eligibility checks for service regime governance.</summary>
public sealed class ServiceRegimePayrollBlockService(HRCoreDbContext db)
{
    public const string OpenExceptionReason = "OPEN_SERVICE_REGIME_EXCEPTION";
    public const string MissingRegimeReason = "MISSING_SERVICE_REGIME";

    public async Task<IReadOnlyList<PayrollBlockerResult>> GetBlockersAsync(
        IReadOnlyList<Guid> employeeIds,
        CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return [];

        var distinct = employeeIds.Distinct().ToList();

        var openExceptions = await db.ServiceRegimeExceptions.AsNoTracking()
            .Where(e => distinct.Contains(e.EmployeeId) && e.Status == ServiceRegimeExceptionStatus.Open)
            .Select(e => e.EmployeeId)
            .Distinct()
            .ToListAsync(ct);

        var missingRegime = await db.EmploymentRecords.AsNoTracking()
            .Where(r => distinct.Contains(r.EmployeeId) && r.EndDate == null && r.LegalRegimeId == null)
            .Select(r => r.EmployeeId)
            .Distinct()
            .ToListAsync(ct);

        var results = new List<PayrollBlockerResult>();
        foreach (var employeeId in distinct)
        {
            if (openExceptions.Contains(employeeId))
            {
                results.Add(new PayrollBlockerResult(employeeId, true, OpenExceptionReason));
                continue;
            }

            if (missingRegime.Contains(employeeId))
            {
                results.Add(new PayrollBlockerResult(employeeId, true, MissingRegimeReason));
                continue;
            }

            results.Add(new PayrollBlockerResult(employeeId, false, null));
        }

        return results;
    }
}

public sealed record PayrollBlockerResult(Guid EmployeeId, bool Blocked, string? ReasonCode);
