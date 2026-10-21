using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.OrgStructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Establishment;

public interface IFundedPositionVersionService
{
    Task AppendFromPositionAsync(
        FundedPosition position,
        EstablishmentOperationType operationType,
        DateTime effectiveFrom,
        Guid? changeRequestId = null,
        string? legalSourceId = null,
        string? decisionReference = null,
        CancellationToken ct = default);

    Task<FundedPositionVersionDto?> ResolveAsOfAsync(Guid fundedPositionId, DateTime asOf, CancellationToken ct = default);

    Task<IReadOnlyList<FundedPositionVersionDto>> ListHistoryAsync(Guid fundedPositionId, CancellationToken ct = default);

    Task<IReadOnlyList<FundedPositionAsOfSnapshot>> ListPositionsAsOfAsync(Guid orgUnitId, DateTime asOf, CancellationToken ct = default);

    Task<int> BackfillMissingVersionsAsync(CancellationToken ct = default);
}

public sealed class FundedPositionVersionDto
{
    public Guid Id { get; init; }
    public Guid FundedPositionId { get; init; }
    public DateTime ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
    public string PositionCode { get; init; } = string.Empty;
    public string GradeCode { get; init; } = string.Empty;
    public decimal FTE { get; init; }
    public int BudgetYear { get; init; }
    public FundedPositionStatus Status { get; init; }
    public Guid OrgUnitId { get; init; }
    public Guid? JobCatalogEntryId { get; init; }
    public Guid? OccupantEmployeeId { get; init; }
    public string? CostCenter { get; init; }
    public string? FreezeReason { get; init; }
    public EstablishmentOperationType OperationType { get; init; }
    public Guid? ChangeRequestId { get; init; }
    public string? LegalSourceId { get; init; }
    public string? DecisionReference { get; init; }
}

public sealed class FundedPositionVersionService(HRCoreDbContext db) : IFundedPositionVersionService
{
    public async Task AppendFromPositionAsync(
        FundedPosition position,
        EstablishmentOperationType operationType,
        DateTime effectiveFrom,
        Guid? changeRequestId = null,
        string? legalSourceId = null,
        string? decisionReference = null,
        CancellationToken ct = default)
    {
        effectiveFrom = effectiveFrom.ToUniversalTime();

        var open = await db.FundedPositionVersions
            .Where(v => v.FundedPositionId == position.Id && v.ValidTo == null)
            .OrderByDescending(v => v.ValidFrom)
            .FirstOrDefaultAsync(ct);

        if (open is not null)
        {
            open.ValidTo = effectiveFrom;
            open.UpdatedAt = DateTime.UtcNow;
        }

        db.FundedPositionVersions.Add(new FundedPositionVersion
        {
            TenantId = position.TenantId,
            FundedPositionId = position.Id,
            ValidFrom = effectiveFrom,
            ValidTo = null,
            PositionCode = position.PositionCode,
            GradeCode = position.GradeCode,
            FTE = position.FTE,
            BudgetYear = position.BudgetYear,
            Status = position.Status,
            OrgUnitId = position.OrgUnitId,
            JobCatalogEntryId = position.JobCatalogEntryId,
            OccupantEmployeeId = position.OccupantEmployeeId,
            CostCenter = position.CostCenter,
            FreezeReason = position.FreezeReason,
            OperationType = operationType,
            ChangeRequestId = changeRequestId,
            LegalSourceId = legalSourceId,
            DecisionReference = decisionReference
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<FundedPositionVersionDto?> ResolveAsOfAsync(Guid fundedPositionId, DateTime asOf, CancellationToken ct = default)
    {
        asOf = asOf.ToUniversalTime();

        if (!await db.FundedPositions.AsNoTracking().AnyAsync(p => p.Id == fundedPositionId, ct))
            return null;

        var version = await db.FundedPositionVersions.AsNoTracking()
            .Where(v => v.FundedPositionId == fundedPositionId)
            .Where(v => v.ValidFrom <= asOf)
            .Where(v => v.ValidTo == null || v.ValidTo > asOf)
            .OrderByDescending(v => v.ValidFrom)
            .FirstOrDefaultAsync(ct);

        return version is null ? null : ToDto(version);
    }

    public async Task<IReadOnlyList<FundedPositionVersionDto>> ListHistoryAsync(Guid fundedPositionId, CancellationToken ct = default)
    {
        var items = await db.FundedPositionVersions.AsNoTracking()
            .Where(v => v.FundedPositionId == fundedPositionId)
            .OrderByDescending(v => v.ValidFrom)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<FundedPositionAsOfSnapshot>> ListPositionsAsOfAsync(
        Guid orgUnitId,
        DateTime asOf,
        CancellationToken ct = default)
    {
        asOf = asOf.ToUniversalTime();

        var positionIds = await db.FundedPositions.AsNoTracking()
            .Where(p => p.OrgUnitId == orgUnitId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (positionIds.Count == 0) return [];

        var snapshots = new List<FundedPositionAsOfSnapshot>();
        foreach (var id in positionIds)
        {
            var version = await ResolveAsOfAsync(id, asOf, ct);
            if (version is null) continue;

            snapshots.Add(new FundedPositionAsOfSnapshot
            {
                Id = version.FundedPositionId,
                PositionCode = version.PositionCode,
                GradeCode = version.GradeCode,
                Status = version.Status,
                OccupantEmployeeId = version.OccupantEmployeeId
            });
        }

        return snapshots.OrderBy(p => p.PositionCode).ToList();
    }

    public async Task<int> BackfillMissingVersionsAsync(CancellationToken ct = default)
    {
        var positions = await db.FundedPositions
            .Where(p => !db.FundedPositionVersions.Any(v => v.FundedPositionId == p.Id))
            .ToListAsync(ct);

        foreach (var position in positions)
        {
            db.FundedPositionVersions.Add(new FundedPositionVersion
            {
                TenantId = position.TenantId,
                FundedPositionId = position.Id,
                ValidFrom = position.CreatedAt == default ? DateTime.UtcNow.AddYears(-1) : position.CreatedAt,
                ValidTo = null,
                PositionCode = position.PositionCode,
                GradeCode = position.GradeCode,
                FTE = position.FTE,
                BudgetYear = position.BudgetYear,
                Status = position.Status,
                OrgUnitId = position.OrgUnitId,
                JobCatalogEntryId = position.JobCatalogEntryId,
                OccupantEmployeeId = position.OccupantEmployeeId,
                CostCenter = position.CostCenter,
                FreezeReason = position.FreezeReason,
                OperationType = EstablishmentOperationType.PositionCreated
            });
        }

        if (positions.Count == 0) return 0;
        await db.SaveChangesAsync(ct);
        return positions.Count;
    }

    private static FundedPositionVersionDto ToDto(FundedPositionVersion v) => new()
    {
        Id = v.Id,
        FundedPositionId = v.FundedPositionId,
        ValidFrom = v.ValidFrom,
        ValidTo = v.ValidTo,
        PositionCode = v.PositionCode,
        GradeCode = v.GradeCode,
        FTE = v.FTE,
        BudgetYear = v.BudgetYear,
        Status = v.Status,
        OrgUnitId = v.OrgUnitId,
        JobCatalogEntryId = v.JobCatalogEntryId,
        OccupantEmployeeId = v.OccupantEmployeeId,
        CostCenter = v.CostCenter,
        FreezeReason = v.FreezeReason,
        OperationType = v.OperationType,
        ChangeRequestId = v.ChangeRequestId,
        LegalSourceId = v.LegalSourceId,
        DecisionReference = v.DecisionReference
    };
}
