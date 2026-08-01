using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>TEN-002: classification, IdempotencyRecord filter, RLS migrations, IgnoreQueryFilters guard.</summary>
public class Ten002ArchitecturalTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "TENANT_ENTITY_CLASSIFICATION.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private static readonly string[] ServiceMigrationDirs =
    [
        "src/Services/HRCore/HR.Core.Infrastructure/Migrations",
        "src/Services/HRContracts/HR.Contracts.Infrastructure/Migrations",
        "src/Services/Payroll/Payroll.Infrastructure/Migrations",
        "src/Services/HRMovements/HR.Movements.Infrastructure/Migrations",
        "src/Services/HRTraining/HR.Training.Infrastructure/Migrations",
        "src/Services/HRHealth/HR.Health.Infrastructure/Migrations",
        "src/Services/Compliance/Compliance.Infrastructure/Migrations",
        "src/Services/RecordsArchive/Records.Archive.Infrastructure/Migrations",
        "src/Services/IDCard/IDCard.Infrastructure/Migrations",
        "src/Services/FileStorage/File.Storage.Infrastructure/Migrations",
        "src/Services/ITInfrastructure/IT.Infra.Infrastructure/Migrations",
        "src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Migrations",
        "src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Migrations",
        "src/Services/AuthDirectory/Auth.Directory.Infrastructure/Migrations"
    ];

    [Fact]
    public void Classification_doc_exists_and_covers_categories()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "TENANT_ENTITY_CLASSIFICATION.md");
        Assert.True(System.IO.File.Exists(path));
        var text = System.IO.File.ReadAllText(path);
        Assert.Contains("global", text);
        Assert.Contains("tenant-owned", text);
        Assert.Contains("IdempotencyRecord", text);
        Assert.Contains("EntityAuditLog", text);
        Assert.Contains("TEN002B", text);
    }

    [Fact]
    public void Global_entities_are_explicitly_classified()
    {
        Assert.True(TenantEntityClassification.IsGlobalEntity(typeof(EntityAuditLog)));
        Assert.True(TenantEntityClassification.IsGlobalEntity(typeof(Tenant)));
        Assert.False(TenantEntityClassification.IsTenantOwned(typeof(EntityAuditLog)));
        Assert.True(TenantEntityClassification.IsTenantOwned(typeof(IntegrationMessageReceipt)));
        Assert.False(TenantEntityClassification.IsGlobalEntity(typeof(IdempotencyRecord)));
    }

    [Fact]
    public void IdempotencyRecord_gets_tenant_query_filter()
    {
        var options = new DbContextOptionsBuilder<FilterProbeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenant = new FixedTenantContext(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        using var db = new FilterProbeDbContext(options, tenant);

        var entityType = db.Model.FindEntityType(typeof(IdempotencyRecord));
        Assert.NotNull(entityType);
        Assert.NotEmpty(entityType!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void Every_service_has_TEN002B_ExtendRls_migration()
    {
        var root = ResolveRepoRoot();
        foreach (var rel in ServiceMigrationDirs)
        {
            var dir = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(dir), $"missing {rel}");
            var hit = Directory.GetFiles(dir, "*TEN002B_ExtendRls.cs");
            Assert.True(hit.Length >= 1, $"missing TEN002B_ExtendRls in {rel}");
            var text = System.IO.File.ReadAllText(hit[0]);
            Assert.Contains("EnableRlsOnAllTenantIdTables", text);
        }
    }

    [Fact]
    public void IgnoreQueryFilters_only_in_sanctioned_bypass()
    {
        var root = ResolveRepoRoot();
        var srcRoot = Path.Combine(root, "src");
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;
            if (file.EndsWith("TenantFilterBypass.cs", StringComparison.OrdinalIgnoreCase))
                continue;
            // Designer / generated snapshots may mention filters in comments rarely — skip Migrations Designers
            if (file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = System.IO.File.ReadAllText(file);
            if (Regex.IsMatch(text, @"\.IgnoreQueryFilters\s*\("))
                violations.Add(Path.GetRelativePath(root, file));
        }

        Assert.True(violations.Count == 0,
            "IgnoreQueryFilters must only appear in TenantFilterBypass.cs. Found:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void Traceability_reflects_enforced_TenantId()
    {
        var path = Path.Combine(ResolveRepoRoot(), "docs", "completion-backlog", "05-TRACEABILITY.md");
        var text = System.IO.File.ReadAllText(path);
        Assert.DoesNotContain("TenantId اختياري دون enforcement", text);
        Assert.Contains("TenantId", text);
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    private sealed class FilterProbeDbContext : TenantAwareDbContext
    {
        public FilterProbeDbContext(DbContextOptions options, ITenantContext tenantContext)
            : base(options, tenantContext) { }

        public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureIdempotencyRecords();
            modelBuilder.ConfigureTenantQueryFilters(this);
            base.OnModelCreating(modelBuilder);
        }
    }
}
