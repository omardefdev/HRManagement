using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Infrastructure.Data;

public static class AcceptanceLogisticsSeeder
{
    public static Task SeedAsync(AdminLogisticsDbContext db, CancellationToken ct = default) =>
        AcceptanceSeedRunner.ForEachAgencyAsync(
            db,
            async (context, token) =>
            {
                await SeedSnapshotsAsync(context, token);
                await DemoLogisticsSeeder.SeedAsync(context, token);
                if (AcceptanceSeedExecution.CurrentAgency?.Id == AcceptanceSeedCatalog.AgencyAId)
                    await SeedPrivilegedAssetAsync(context, token);
            },
            ct);

    private static async Task SeedSnapshotsAsync(AdminLogisticsDbContext db, CancellationToken ct)
    {
        var agency = AcceptanceSeedExecution.CurrentAgency
            ?? throw new InvalidOperationException("Acceptance agency context is required.");

        foreach (var source in agency.OrgUnits)
        {
            if (await db.OrgUnitSnapshots.AnyAsync(o => o.Id == source.Id, ct))
                continue;

            db.OrgUnitSnapshots.Add(new OrgUnitSnapshot
            {
                Id = source.Id,
                TenantId = agency.Id,
                ParentId = source.ParentId,
                Code = source.Code,
                NameAr = source.NameAr,
                MaterializedPath = source.MaterializedPath
            });
        }

        foreach (var employee in agency.Employees)
        {
            if (await db.EmployeeOrgUnitSnapshots.AnyAsync(e => e.EmployeeId == employee.Id, ct))
                continue;

            db.EmployeeOrgUnitSnapshots.Add(new EmployeeOrgUnitSnapshot
            {
                EmployeeId = employee.Id,
                TenantId = agency.Id,
                PrimaryOrgUnitId = employee.OrgUnitId
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPrivilegedAssetAsync(AdminLogisticsDbContext db, CancellationToken ct)
    {
        if (await db.Assets.AnyAsync(a => a.Id == AcceptanceSeedIds.AcceptanceAssetBr1Id, ct))
            return;

        var now = AcceptanceSeedCatalog.ReferenceDate;
        db.Assets.Add(new Asset
        {
            Id = AcceptanceSeedIds.AcceptanceAssetBr1Id,
            TenantId = AcceptanceSeedCatalog.AgencyAId,
            TagNumber = AcceptanceSeedIds.AcceptanceAssetBr1Tag,
            Name = "حاسوب محمول مميز - فرع ريف دمشق",
            Category = "حواسيب",
            Brand = "Dell",
            Status = AssetStatus.InUse,
            IsPrivileged = true,
            Value = 7_500_000m,
            IsLegacyEntry = true,
            LegacyEntryReason = "أصل قبول مميز لاختبار نطاق العهدة ومسار الفقد",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.AssetAssignments.Add(new AssetAssignment
        {
            Id = AcceptanceSeedIds.AcceptanceAssetBr1AssignmentId,
            TenantId = AcceptanceSeedCatalog.AgencyAId,
            AssetId = AcceptanceSeedIds.AcceptanceAssetBr1Id,
            EmployeeId = AcceptanceSeedIds.EmpBr1Id,
            AssignedAt = now.AddDays(-30),
            Status = AssetAssignmentStatus.Active,
            HandoverCondition = AssetHandoverCondition.Good,
            EvidenceRef = "AC-32-AC-33-HANDOVER",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(ct);
    }
}
