using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class CodSequenceBootstrapTests
{
    private static HRCoreDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HRCoreDbContext(options);
    }

    [Fact]
    public async Task BootstrapFromObservedCodes_is_idempotent()
    {
        await using var db = CreateDb();
        var service = new CodSequenceBootstrapService(db);
        var codes = new[] { "AH-2026-000042", "AH-2026-000010", "IT-2026-000007" };

        var first = await service.BootstrapFromObservedCodesAsync(
            TenantDefaults.DefaultTenantId, CodeEntityKind.AssetTag, codes);
        var countAfterFirst = await db.CodeSequenceCounters.CountAsync();

        var second = await service.BootstrapFromObservedCodesAsync(
            TenantDefaults.DefaultTenantId, CodeEntityKind.AssetTag, codes);
        var counter = await db.CodeSequenceCounters.SingleAsync(c =>
            c.EntityKind == CodeEntityKind.AssetTag && c.Year == 2026);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(42, counter.LastValue);
        Assert.Equal(countAfterFirst, await db.CodeSequenceCounters.CountAsync());
    }

    [Fact]
    public async Task BootstrapFromObservedCodes_skips_demo_prefixes()
    {
        await using var db = CreateDb();
        var service = new CodSequenceBootstrapService(db);

        var upserts = await service.BootstrapFromObservedCodesAsync(
            TenantDefaults.DefaultTenantId,
            CodeEntityKind.AssetTag,
            ["DEMO-00001", "IT-DEV-00001"]);

        Assert.Equal(0, upserts);
        Assert.False(await db.CodeSequenceCounters.AnyAsync());
    }

    [Fact]
    public async Task BootstrapFromObservedCodes_parses_inventory_sku()
    {
        await using var db = CreateDb();
        var service = new CodSequenceBootstrapService(db);

        var upserts = await service.BootstrapFromObservedCodesAsync(
            TenantDefaults.DefaultTenantId,
            CodeEntityKind.InventorySku,
            ["SKU-2026-000042", "SKU-2026-000010"]);

        var counter = await db.CodeSequenceCounters.SingleAsync(c =>
            c.EntityKind == CodeEntityKind.InventorySku && c.Year == 2026);

        Assert.Equal(1, upserts);
        Assert.Equal(42, counter.LastValue);
    }
}
