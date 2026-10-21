using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Establishment;

public interface IJobCatalogVersionService
{
    Task AppendFromEntryAsync(
        JobCatalogEntry entry,
        EstablishmentOperationType operationType,
        DateTime effectiveFrom,
        Guid? changeRequestId = null,
        string? legalSourceId = null,
        string? decisionReference = null,
        CancellationToken ct = default);

    Task<JobCatalogVersionDto?> ResolveAsOfAsync(Guid jobCatalogEntryId, DateTime asOf, CancellationToken ct = default);

    Task<IReadOnlyList<JobCatalogVersionDto>> ListHistoryAsync(Guid jobCatalogEntryId, CancellationToken ct = default);

    Task<int> BackfillMissingVersionsAsync(CancellationToken ct = default);
}

public sealed class JobCatalogVersionDto
{
    public Guid Id { get; init; }
    public Guid JobCatalogEntryId { get; init; }
    public DateTime ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
    public string JobCode { get; init; } = string.Empty;
    public string TitleAr { get; init; } = string.Empty;
    public string? TitleEn { get; init; }
    public string GradeCode { get; init; } = string.Empty;
    public string? OccupationalGroup { get; init; }
    public bool IsActive { get; init; }
    public EstablishmentOperationType OperationType { get; init; }
    public Guid? ChangeRequestId { get; init; }
    public string? LegalSourceId { get; init; }
    public string? DecisionReference { get; init; }
}

public sealed class JobCatalogVersionService(HRCoreDbContext db) : IJobCatalogVersionService
{
    public async Task AppendFromEntryAsync(
        JobCatalogEntry entry,
        EstablishmentOperationType operationType,
        DateTime effectiveFrom,
        Guid? changeRequestId = null,
        string? legalSourceId = null,
        string? decisionReference = null,
        CancellationToken ct = default)
    {
        effectiveFrom = effectiveFrom.ToUniversalTime();

        var open = await db.JobCatalogVersions
            .Where(v => v.JobCatalogEntryId == entry.Id && v.ValidTo == null)
            .OrderByDescending(v => v.ValidFrom)
            .FirstOrDefaultAsync(ct);

        if (open is not null)
        {
            open.ValidTo = effectiveFrom;
            open.UpdatedAt = DateTime.UtcNow;
        }

        db.JobCatalogVersions.Add(new JobCatalogVersion
        {
            TenantId = entry.TenantId,
            JobCatalogEntryId = entry.Id,
            ValidFrom = effectiveFrom,
            ValidTo = null,
            JobCode = entry.JobCode,
            TitleAr = entry.TitleAr,
            TitleEn = entry.TitleEn,
            GradeCode = entry.GradeCode,
            OccupationalGroup = entry.OccupationalGroup,
            IsActive = entry.IsActive,
            OperationType = operationType,
            ChangeRequestId = changeRequestId,
            LegalSourceId = legalSourceId,
            DecisionReference = decisionReference
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<JobCatalogVersionDto?> ResolveAsOfAsync(Guid jobCatalogEntryId, DateTime asOf, CancellationToken ct = default)
    {
        asOf = asOf.ToUniversalTime();

        if (!await db.JobCatalogEntries.AsNoTracking().AnyAsync(e => e.Id == jobCatalogEntryId, ct))
            return null;

        var version = await db.JobCatalogVersions.AsNoTracking()
            .Where(v => v.JobCatalogEntryId == jobCatalogEntryId)
            .Where(v => v.ValidFrom <= asOf)
            .Where(v => v.ValidTo == null || v.ValidTo > asOf)
            .OrderByDescending(v => v.ValidFrom)
            .FirstOrDefaultAsync(ct);

        return version is null ? null : ToDto(version);
    }

    public async Task<IReadOnlyList<JobCatalogVersionDto>> ListHistoryAsync(Guid jobCatalogEntryId, CancellationToken ct = default)
    {
        var items = await db.JobCatalogVersions.AsNoTracking()
            .Where(v => v.JobCatalogEntryId == jobCatalogEntryId)
            .OrderByDescending(v => v.ValidFrom)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    public async Task<int> BackfillMissingVersionsAsync(CancellationToken ct = default)
    {
        var entries = await db.JobCatalogEntries
            .Where(e => !db.JobCatalogVersions.Any(v => v.JobCatalogEntryId == e.Id))
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            db.JobCatalogVersions.Add(new JobCatalogVersion
            {
                TenantId = entry.TenantId,
                JobCatalogEntryId = entry.Id,
                ValidFrom = entry.CreatedAt == default ? DateTime.UtcNow.AddYears(-1) : entry.CreatedAt,
                ValidTo = null,
                JobCode = entry.JobCode,
                TitleAr = entry.TitleAr,
                TitleEn = entry.TitleEn,
                GradeCode = entry.GradeCode,
                OccupationalGroup = entry.OccupationalGroup,
                IsActive = entry.IsActive,
                OperationType = EstablishmentOperationType.Create
            });
        }

        if (entries.Count == 0) return 0;
        await db.SaveChangesAsync(ct);
        return entries.Count;
    }

    private static JobCatalogVersionDto ToDto(JobCatalogVersion v) => new()
    {
        Id = v.Id,
        JobCatalogEntryId = v.JobCatalogEntryId,
        ValidFrom = v.ValidFrom,
        ValidTo = v.ValidTo,
        JobCode = v.JobCode,
        TitleAr = v.TitleAr,
        TitleEn = v.TitleEn,
        GradeCode = v.GradeCode,
        OccupationalGroup = v.OccupationalGroup,
        IsActive = v.IsActive,
        OperationType = v.OperationType,
        ChangeRequestId = v.ChangeRequestId,
        LegalSourceId = v.LegalSourceId,
        DecisionReference = v.DecisionReference
    };
}
