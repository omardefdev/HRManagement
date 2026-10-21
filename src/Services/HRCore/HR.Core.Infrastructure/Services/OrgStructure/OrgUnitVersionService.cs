using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using Microsoft.EntityFrameworkCore;



namespace HR.Core.Infrastructure.Services.OrgStructure;



public sealed class OrgUnitVersionService(

    HRCoreDbContext db,

    IFundedPositionVersionService fundedPositionVersions) : IOrgUnitVersionService

{

    public async Task AppendVersionAsync(

        OrgUnit unit,

        OrgOperationType operationType,

        DateTime effectiveFrom,

        string? approvedBy = null,

        string? decisionReference = null,

        CancellationToken cancellationToken = default)

    {

        var open = await db.OrgUnitVersions

            .Where(v => v.OrgUnitId == unit.Id && v.ValidTo == null)

            .OrderByDescending(v => v.ValidFrom)

            .FirstOrDefaultAsync(cancellationToken);



        if (open is not null)

        {

            open.ValidTo = effectiveFrom;

            open.UpdatedAt = DateTime.UtcNow;

        }



        db.OrgUnitVersions.Add(new OrgUnitVersion

        {

            TenantId = unit.TenantId,

            OrgUnitId = unit.Id,

            ValidFrom = effectiveFrom,

            ValidTo = null,

            Name = unit.NameAr,

            NameEn = unit.NameEn,

            ParentId = unit.ParentId,

            HeadEmployeeId = unit.HeadEmployeeId,

            OperationType = operationType,

            ApprovedBy = approvedBy,

            DecisionReference = decisionReference

        });



        await db.SaveChangesAsync(cancellationToken);

    }



    public async Task<OrgStructureAsOfDto?> ResolveAsOfAsync(

        Guid orgUnitId,

        DateTime asOf,

        CancellationToken cancellationToken = default)

    {

        var unit = await db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgUnitId, cancellationToken);

        if (unit is null) return null;



        var version = await db.OrgUnitVersions.AsNoTracking()

            .Where(v => v.OrgUnitId == orgUnitId)

            .Where(v => v.ValidFrom <= asOf)

            .Where(v => v.ValidTo == null || v.ValidTo > asOf)

            .OrderByDescending(v => v.ValidFrom)

            .FirstOrDefaultAsync(cancellationToken);



        if (version is null) return null;



        HeadEmployeeAsOfSnapshot? head = null;

        if (version.HeadEmployeeId.HasValue)

        {

            var employee = await db.Employees.AsNoTracking()

                .FirstOrDefaultAsync(e => e.Id == version.HeadEmployeeId.Value, cancellationToken);

            if (employee is not null)

            {

                head = new HeadEmployeeAsOfSnapshot

                {

                    Id = employee.Id,

                    Name = employee.FullName

                };

            }

        }



        var positions = await fundedPositionVersions.ListPositionsAsOfAsync(orgUnitId, asOf, cancellationToken);



        return new OrgStructureAsOfDto

        {

            OrgUnitId = orgUnitId,

            AsOf = asOf,

            OrgUnit = new OrgUnitAsOfSnapshot

            {

                Id = unit.Id,

                Code = unit.Code,

                NameAr = version.Name,

                NameEn = version.NameEn,

                ParentId = version.ParentId,

                HeadEmployeeId = version.HeadEmployeeId,

                OperationType = version.OperationType,

                ValidFrom = version.ValidFrom,

                ValidTo = version.ValidTo

            },

            HeadEmployee = head,

            FundedPositions = positions

        };

    }



    public async Task<int> BackfillMissingVersionsAsync(CancellationToken cancellationToken = default)

    {

        var unitsWithoutVersion = await db.OrgUnits

            .Where(u => !db.OrgUnitVersions.Any(v => v.OrgUnitId == u.Id))

            .ToListAsync(cancellationToken);



        foreach (var unit in unitsWithoutVersion)

        {

            db.OrgUnitVersions.Add(new OrgUnitVersion

            {

                TenantId = unit.TenantId,

                OrgUnitId = unit.Id,

                ValidFrom = unit.CreatedAt == default ? DateTime.UtcNow.AddYears(-1) : unit.CreatedAt,

                ValidTo = null,

                Name = unit.NameAr,

                NameEn = unit.NameEn,

                ParentId = unit.ParentId,

                HeadEmployeeId = unit.HeadEmployeeId,

                OperationType = OrgOperationType.Create

            });

        }



        if (unitsWithoutVersion.Count == 0)

            return 0;



        await db.SaveChangesAsync(cancellationToken);

        return unitsWithoutVersion.Count;

    }

}


