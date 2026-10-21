using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

internal static class EstablishmentMdmTestHelper
{
    public static async Task SeedReferenceCatalogAsync(HRCoreDbContext db)
    {
        if (await db.ReferenceCatalogEntries.AnyAsync())
            return;

        var tenantId = TenantDefaults.DefaultTenantId;
        db.ReferenceCatalogEntries.AddRange(
            new ReferenceCatalogEntry
            {
                TenantId = tenantId,
                CatalogKind = CatalogKind.Grade,
                Code = "GR-05",
                NameAr = "الدرجة الخامسة",
                IsActive = true
            },
            new ReferenceCatalogEntry
            {
                TenantId = tenantId,
                CatalogKind = CatalogKind.Grade,
                Code = "G5",
                NameAr = "درجة G5",
                IsActive = true
            },
            new ReferenceCatalogEntry
            {
                TenantId = tenantId,
                CatalogKind = CatalogKind.JobClassification,
                Code = "JC-ADM",
                NameAr = "إداري",
                IsActive = true
            });

        await db.SaveChangesAsync();
    }
}
