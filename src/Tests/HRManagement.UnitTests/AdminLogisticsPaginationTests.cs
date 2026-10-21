using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class AdminLogisticsPaginationTests
{
    private static async Task<AdminLogisticsDbContext> SeedVendorsAsync(int count)
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);

        for (var i = 1; i <= count; i++)
        {
            db.Vendors.Add(new Vendor
            {
                Id = Guid.NewGuid(),
                Name = $"مورد {i:D3}",
                Country = i % 2 == 0 ? "سوريا" : "الأردن"
            });
        }

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task VendorIndex_page1_returns_default_page_size()
    {
        await using var db = await SeedVendorsAsync(30);

        var filter = new VendorIndexFilter { Page = 1, PageSize = SearchFilterBase.DefaultPageSize };
        var (page, pageSize) = filter.NormalizePaging();

        var paged = await filter.Apply(db.Vendors.AsNoTracking())
            .OrderBy(v => v.Name)
            .ToPaginatedListAsync(page, pageSize);

        Assert.Equal(30, paged.TotalCount);
        Assert.Equal(25, paged.Items.Count);
        Assert.Equal(2, paged.TotalPages);
        Assert.True(paged.HasNextPage);
        Assert.False(paged.HasPreviousPage);
    }

    [Fact]
    public async Task VendorIndex_page2_returns_remaining_items()
    {
        await using var db = await SeedVendorsAsync(30);

        var filter = new VendorIndexFilter { Page = 2, PageSize = SearchFilterBase.DefaultPageSize };
        var (page, pageSize) = filter.NormalizePaging();

        var paged = await filter.Apply(db.Vendors.AsNoTracking())
            .OrderBy(v => v.Name)
            .ToPaginatedListAsync(page, pageSize);

        Assert.Equal(5, paged.Items.Count);
        Assert.True(paged.HasPreviousPage);
        Assert.False(paged.HasNextPage);
    }

    [Fact]
    public async Task VendorIndex_filter_and_pagination_combined()
    {
        await using var db = await SeedVendorsAsync(30);

        var filter = new VendorIndexFilter
        {
            Country = "سوريا",
            Page = 1,
            PageSize = SearchFilterBase.DefaultPageSize
        };
        var (page, pageSize) = filter.NormalizePaging();

        var paged = await filter.Apply(db.Vendors.AsNoTracking())
            .OrderBy(v => v.Name)
            .ToPaginatedListAsync(page, pageSize);

        Assert.Equal(15, paged.TotalCount);
        Assert.Equal(15, paged.Items.Count);
        Assert.All(paged.Items, v => Assert.Equal("سوريا", v.Country));
    }

    [Fact]
    public async Task InventoryItemIndex_pagination()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AdminLogisticsDbContext(options);

        for (var i = 1; i <= 30; i++)
        {
            db.InventoryItems.Add(new InventoryItem
            {
                Id = Guid.NewGuid(),
                Sku = $"SKU-2026-{i:D6}",
                Name = $"عنصر {i}",
                Quantity = i
            });
        }
        await db.SaveChangesAsync();

        var filter = new InventoryItemIndexFilter { Page = 2, PageSize = SearchFilterBase.DefaultPageSize };
        var (page, pageSize) = filter.NormalizePaging();

        var paged = await filter.Apply(db.InventoryItems.AsNoTracking())
            .OrderBy(x => x.Sku)
            .ToPaginatedListAsync(page, pageSize);

        Assert.Equal(30, paged.TotalCount);
        Assert.Equal(5, paged.Items.Count);
        Assert.Equal(2, paged.PageIndex);
    }

    [Fact]
    public void AdminLogisticsPagedFilter_clamps_invalid_values()
    {
        var filter = new VendorIndexFilter { Page = 0, PageSize = 500 };
        var (page, pageSize) = filter.NormalizePaging();

        Assert.Equal(1, page);
        Assert.Equal(SearchFilterBase.MaxPageSize, pageSize);
    }
}
