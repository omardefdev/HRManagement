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

        await EnsureSeededJobCatalogGradesAsync(db, ct);
    }

    /// <summary>
    /// PLAN-41: the seeded job catalog uses G1..G10 (AcceptanceDataSeeder), so each agency owns those grades; a grade in use
    /// must be there for the deactivation impact check of AC-EST-18d, which a national row would never reach.
    /// </summary>
    private static async Task EnsureSeededJobCatalogGradesAsync(HRCoreDbContext db, CancellationToken ct)
    {
        foreach (var agency in AcceptanceSeedCatalog.Agencies)
        {
            var previousTenantId = TenantIdFlow.Get();
            TenantIdFlow.Set(agency.Id);
            try
            {
                var existing = await db.ReferenceCatalogEntries
                    .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                    .Where(e => e.TenantId == agency.Id && e.CatalogKind == CatalogKind.Grade)
                    .Select(e => e.Code)
                    .ToListAsync(ct);
                foreach (var code in Enumerable.Range(1, 10).Select(n => $"G{n}").Except(existing))
                {
                    db.ReferenceCatalogEntries.Add(new ReferenceCatalogEntry
                    {
                        TenantId = agency.Id,
                        CatalogKind = CatalogKind.Grade,
                        Code = code,
                        NameAr = $"درجة قبول {code}",
                        IsActive = true,
                        Version = 1
                    });
                }

                await db.SaveChangesAsync(ct);
            }
            finally
            {
                TenantIdFlow.Set(previousTenantId);
            }
        }
    }
}
