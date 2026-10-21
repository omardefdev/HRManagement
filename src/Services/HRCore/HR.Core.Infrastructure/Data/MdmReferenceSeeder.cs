using System.Text.Json;
using System.Text.Json.Serialization;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Seeding;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

/// <summary>SEED-001: loads national MDM reference catalog from versioned JSON pack.</summary>
public static class MdmReferenceSeeder
{
    private sealed class CatalogPack
    {
        public string PackId { get; set; } = "";
        public List<CatalogEntryDto> Entries { get; set; } = [];
    }

    private sealed class CatalogEntryDto
    {
        public string Kind { get; set; } = "";
        public string Code { get; set; } = "";
        public string NameAr { get; set; } = "";
        public string? NameEn { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
        => await SeedAsync(db, TenantDefaults.DefaultTenantId, ct);

    public static async Task SeedAsync(HRCoreDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var pack = LoadPack();
        foreach (var entry in pack.Entries)
        {
            if (!Enum.TryParse<CatalogKind>(entry.Kind, ignoreCase: true, out var kind))
                continue;

            if (await db.ReferenceCatalogEntries.AnyAsync(
                    e => e.TenantId == tenantId && e.CatalogKind == kind && e.Code == entry.Code, ct))
                continue;

            db.ReferenceCatalogEntries.Add(new ReferenceCatalogEntry
            {
                TenantId = tenantId,
                CatalogKind = kind,
                Code = entry.Code,
                NameAr = entry.NameAr,
                NameEn = entry.NameEn,
                IsActive = true,
                Version = 1,
                SourceAuthority = SeedPackIds.NationalMdmSyria
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task CopySubsetAsync(
        HRCoreDbContext db,
        Guid sourceTenantId,
        Guid targetTenantId,
        IEnumerable<string> catalogKindNames,
        CancellationToken ct = default)
    {
        var kinds = catalogKindNames
            .Select(ParseCatalogKind)
            .Where(k => k.HasValue)
            .Select(k => k!.Value)
            .Distinct()
            .ToList();

        if (kinds.Count == 0)
            return;

        var sourceEntries = await db.ReferenceCatalogEntries.AsNoTracking()
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.TenantProvisioning)
            .Where(e => e.TenantId == sourceTenantId && kinds.Contains(e.CatalogKind))
            .ToListAsync(ct);

        foreach (var src in sourceEntries)
        {
            if (await db.ReferenceCatalogEntries
                    .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.TenantProvisioning)
                    .AnyAsync(
                    e => e.TenantId == targetTenantId && e.CatalogKind == src.CatalogKind && e.Code == src.Code, ct))
                continue;

            db.ReferenceCatalogEntries.Add(new ReferenceCatalogEntry
            {
                TenantId = targetTenantId,
                CatalogKind = src.CatalogKind,
                Code = src.Code,
                NameAr = src.NameAr,
                NameEn = src.NameEn,
                NameFr = src.NameFr,
                IsActive = src.IsActive,
                Version = src.Version,
                SourceAuthority = src.SourceAuthority
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static CatalogKind? ParseCatalogKind(string name) => name.ToLowerInvariant() switch
    {
        "grade" => CatalogKind.Grade,
        "jobclassification" or "employment-type" or "job-classification" => CatalogKind.JobClassification,
        "geography" or "governorate" => CatalogKind.Geography,
        "agency" => CatalogKind.Agency,
        "qualification" => CatalogKind.Qualification,
        "bank" => CatalogKind.Bank,
        "currency" => CatalogKind.Currency,
        "salarycomponent" => CatalogKind.SalaryComponent,
        "documenttype" => CatalogKind.DocumentType,
        "contracttype" => CatalogKind.ContractType,
        "movementtype" => CatalogKind.MovementType,
        "servicetype" => CatalogKind.ServiceType,
        _ => Enum.TryParse<CatalogKind>(name, ignoreCase: true, out var k) ? k : null
    };

    private static CatalogPack LoadPack()
    {
        foreach (var path in ResolvePackPaths())
        {
            if (!File.Exists(path))
                continue;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CatalogPack>(json, JsonOptions)
                   ?? throw new InvalidOperationException($"Invalid MDM pack JSON: {path}");
        }

        throw new FileNotFoundException("National MDM pack not found (seed-packs/national-mdm-sy-v1/catalog.json)");
    }

    private static IEnumerable<string> ResolvePackPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "seed-packs", "national-mdm-sy-v1", "catalog.json");

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            yield return Path.Combine(dir, "seed-packs", "national-mdm-sy-v1", "catalog.json");
            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;
            dir = parent.FullName;
        }
    }
}
