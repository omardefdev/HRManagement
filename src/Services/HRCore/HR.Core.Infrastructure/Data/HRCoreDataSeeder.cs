using HR.Core.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.NationalScope;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Data;

public static class HRCoreDataSeeder
{
    private static readonly Guid RootOrgId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    private static readonly Guid DeptHrId = Guid.Parse("11111111-1111-1111-1111-111111111102");

    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
        => await SeedAsync(db, serviceProvider: null, ct);

    public static async Task SeedAsync(
        HRCoreDbContext db,
        IServiceProvider? serviceProvider,
        CancellationToken ct = default)
    {
        await SeedDefaultTenantAsync(db, serviceProvider, ct);
        await SeedShiftsAsync(db, ct);
        await SeedPublicJobPostingsAsync(db, serviceProvider, ct);

        if (await db.OrgUnits
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .AnyAsync(ct))
            return;

        var root = new OrgUnit
        {
            Id = RootOrgId,
            TenantId = TenantDefaults.DefaultTenantId,
            Code = "ROOT",
            NameAr = "الهيكل التنظيمي الرئيسي",
            NameEn = "Root Organization",
            ParentId = null,
            HeadEmployeeId = null
        };

        var hr = new OrgUnit
        {
            Id = DeptHrId,
            TenantId = TenantDefaults.DefaultTenantId,
            Code = "HR",
            NameAr = "شعبة الموارد البشرية",
            NameEn = "Human Resources",
            ParentId = RootOrgId,
            HeadEmployeeId = null
        };

        db.OrgUnits.AddRange(root, hr);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDefaultTenantAsync(
        HRCoreDbContext db,
        IServiceProvider? serviceProvider,
        CancellationToken ct)
    {
        if (await db.Tenants
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .AnyAsync(t => t.Id == TenantDefaults.DefaultTenantId, ct))
            return;

        var nextCount = await db.Tenants
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .LongCountAsync(ct) + 1;
        var guard = serviceProvider?.GetService<INationalScopeGuard>();
        var err = guard?.CheckTenantCount(nextCount);
        if (err is not null)
        {
            var logger = serviceProvider?.GetService<ILoggerFactory>()?.CreateLogger("HRCoreDataSeeder");
            logger?.LogError("STR-001 blocked default tenant seed: {Error}", err);
            throw new InvalidOperationException(err);
        }

        db.Tenants.Add(new Tenant
        {
            Id = TenantDefaults.DefaultTenantId,
            Code = TenantDefaults.DefaultCode,
            NameAr = TenantDefaults.DefaultNameAr,
            NameEn = "Default Platform Tenant",
            IsActive = true
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedShiftsAsync(HRCoreDbContext db, CancellationToken ct)
    {
        if (await db.Shifts
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .AnyAsync(ct))
            return;

        db.Shifts.AddRange(
            new Shift { TenantId = TenantDefaults.DefaultTenantId, Code = "DAY", NameAr = "الوردية النهارية", ShiftType = HR.Core.Domain.Enums.ShiftType.Day, StartTime = new(8, 0), EndTime = new(16, 0) },
            new Shift { TenantId = TenantDefaults.DefaultTenantId, Code = "EVE", NameAr = "الوردية المسائية", ShiftType = HR.Core.Domain.Enums.ShiftType.Evening, StartTime = new(16, 0), EndTime = new(0, 0) },
            new Shift { TenantId = TenantDefaults.DefaultTenantId, Code = "NIGHT", NameAr = "الوردية الليلية", ShiftType = HR.Core.Domain.Enums.ShiftType.Night, StartTime = new(0, 0), EndTime = new(8, 0) });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPublicJobPostingsAsync(
        HRCoreDbContext db,
        IServiceProvider? serviceProvider,
        CancellationToken ct)
    {
        var config = serviceProvider?.GetService<IConfiguration>();
        if (config?.GetValue("Seed:IncludeSamplePosting", false) != true)
            return;

        if (await db.PublicJobPostings
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .AnyAsync(ct))
            return;

        db.PublicJobPostings.Add(new PublicJobPosting
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Title = "وظيفة عامة تجريبية",
            Description = "وصف تجريبي",
            IsPublished = true
        });

        await db.SaveChangesAsync(ct);
    }
}
