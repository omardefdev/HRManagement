using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>
/// Ensures acceptance agency tenants receive national MDM subsets (Grade, JobClassification)
/// required by establishment pickers and PLAN-19 validation.
/// </summary>
public static class AcceptanceMdmReferencePatch
{
    private static readonly string[] CatalogKindNames =
    [
        nameof(CatalogKind.Grade),
        nameof(CatalogKind.JobClassification)
    ];

    public static async Task ApplyAsync(HRCoreDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        foreach (var agency in AcceptanceSeedCatalog.Agencies)
        {
            var hasGrades = await db.ReferenceCatalogEntries
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .AnyAsync(
                    e => e.TenantId == agency.Id
                         && e.CatalogKind == CatalogKind.Grade
                         && e.IsActive,
                    ct);
            if (hasGrades)
                continue;

            var previousTenantId = TenantIdFlow.Get();
            TenantIdFlow.Set(agency.Id);
            try
            {
                await MdmReferenceSeeder.CopySubsetAsync(
                    db,
                    TenantDefaults.DefaultTenantId,
                    agency.Id,
                    CatalogKindNames,
                    ct);
            }
            finally
            {
                TenantIdFlow.Set(previousTenantId);
            }

            logger?.LogInformation(
                "Copied national MDM reference subset to acceptance agency {AgencyCode}",
                agency.Code);
        }
    }
}
