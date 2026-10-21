using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class CodeGenerationTests
{
    private static HRCoreDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HRCoreDbContext(options);
    }

    private static CodeGeneratorService CreateGenerator(HRCoreDbContext db) =>
        new(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())), Microsoft.Extensions.Logging.Abstractions.NullLogger<CodeGeneratorService>.Instance);

    [Fact]
    public async Task Generate_increments_sequence_atomically()
    {
        await using var db = CreateDb();
        var generator = CreateGenerator(db);

        var r1 = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.AssetTag,
            TenantId = TenantDefaults.DefaultTenantId
        });
        var r2 = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.AssetTag,
            TenantId = TenantDefaults.DefaultTenantId
        });

        Assert.NotEqual(r1.Code, r2.Code);
        Assert.StartsWith("AH-", r1.Code);
    }

    [Fact]
    public async Task OrgUnit_code_uses_parent_prefix()
    {
        await using var db = CreateDb();
        var generator = CreateGenerator(db);

        var result = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.OrgUnit,
            TenantId = TenantDefaults.DefaultTenantId,
            Facts = new Dictionary<string, string> { ["ParentCode"] = "ROOT" }
        });

        Assert.StartsWith("ROOT.", result.Code);
    }

    [Fact]
    public async Task Published_policy_override_from_seeder()
    {
        await using var db = CreateDb();
        await CodeGenerationSeeder.SeedAsync(db);

        var generator = CreateGenerator(db);
        var snapshot = await generator.GetPublishedSnapshotAsync(CodeEntityKind.AssetTag, TenantDefaults.DefaultTenantId);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.Segments.Count >= 3);
    }

    [Fact]
    public async Task PolicyKey_selects_privileged_asset_template()
    {
        await using var db = CreateDb();
        await CodeGenerationSeeder.SeedAsync(db);

        var generator = CreateGenerator(db);
        var result = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.AssetTag,
            TenantId = TenantDefaults.DefaultTenantId,
            PolicyKey = "ASSET-PRIVILEGED"
        });
        Assert.StartsWith("VIP-", result.Code);
    }

    [Fact]
    public async Task Generate_inventorySku_increments_sequence()
    {
        await using var db = CreateDb();
        var generator = CreateGenerator(db);

        var r1 = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.InventorySku,
            TenantId = TenantDefaults.DefaultTenantId
        });
        var r2 = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.InventorySku,
            TenantId = TenantDefaults.DefaultTenantId
        });

        Assert.NotEqual(r1.Code, r2.Code);
        Assert.StartsWith("SKU-", r1.Code);
    }

    [Fact]
    public async Task Seeder_publishes_inventorySku_policy()
    {
        await using var db = CreateDb();
        await CodeGenerationSeeder.SeedAsync(db);

        var generator = CreateGenerator(db);
        var snapshot = await generator.GetPublishedSnapshotAsync(CodeEntityKind.InventorySku, TenantDefaults.DefaultTenantId);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.Segments.Count >= 3);
    }

    [Fact]
    public async Task Pdf_arabic_validate_script_exists()
    {
        var repoRoot = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(repoRoot, "scripts", "code-generation-validate.ps1")));
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "scripts", "code-generation-validate.ps1")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root");
    }
}
