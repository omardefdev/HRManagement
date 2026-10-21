using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Legal;

/// <summary>SYR-007: backfill scan for employment records missing service regime.</summary>
public sealed class ServiceRegimeBackfillService(
    HRCoreDbContext db,
    ServiceRegimeExceptionService exceptionService)
{
    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        var legislationProvider = new LegislationRegisterProvider(db);
        var legalValidator = new RecruitmentLegalSourceValidator(legislationProvider);
        var regimeValidator = new ServiceRegimeValidator(db, legalValidator);
        var transitionService = new ServiceRegimeTransitionService(db, regimeValidator);
        var exceptionService = new ServiceRegimeExceptionService(db, regimeValidator, transitionService);
        await new ServiceRegimeBackfillService(db, exceptionService).EnqueueMissingRegimesAsync(ct);
    }

    public async Task<int> EnqueueMissingRegimesAsync(CancellationToken ct = default)
    {
        var candidates = await db.EmploymentRecords
            .Include(r => r.Employee)
            .Where(r => r.LegalRegimeId == null && r.EndDate == null)
            .ToListAsync(ct);

        var count = 0;
        foreach (var record in candidates)
        {
            if (record.Employee is null)
                continue;

            var created = await exceptionService.EnqueueIfUnclassifiedAsync(
                record.Employee,
                record,
                ServiceRegimeExceptionSource.Backfill,
                legalSourceId: null,
                attemptedRegimeId: null,
                ct);

            if (created is not null)
                count++;
        }

        return count;
    }
}
