using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class GatewayAuditQueryServiceTests
{
    [Fact]
    public async Task Default_query_caps_at_100_items_within_24h()
    {
        await using var db = CreateDb();
        for (var i = 0; i < 150; i++)
        {
            db.AuditEntries.Add(new GatewayAuditEntry
            {
                OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-i),
                CorrelationId = $"corr-{i}",
                Method = "GET",
                Path = "/ui/hr-core",
                QueryString = "",
                StatusCode = 200,
                ElapsedMs = 10,
                Decision = "allowed"
            });
        }
        await db.SaveChangesAsync();

        var svc = new GatewayAuditQueryService(db);
        var result = await svc.SearchAsync(new GatewayAuditSearchRequest(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(100, result.Items.Count);
        Assert.Equal(150, result.TotalCount);
        Assert.True(result.HasMore);
        Assert.False(string.IsNullOrEmpty(result.NextCursor));
    }

    [Fact]
    public async Task Rejects_unbounded_all_time_range()
    {
        await using var db = CreateDb();
        var svc = new GatewayAuditQueryService(db);
        var result = await svc.SearchAsync(new GatewayAuditSearchRequest { TimeRange = "all" }, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Items);
        Assert.Contains("PERF-002", result.Error);
    }

    [Fact]
    public async Task Cursor_page_has_no_overlap_with_first_page()
    {
        await using var db = CreateDb();
        for (var i = 0; i < 30; i++)
        {
            db.AuditEntries.Add(CreateEntry($"c-{i}", 200, DateTimeOffset.UtcNow.AddMinutes(-i)));
        }
        await db.SaveChangesAsync();

        var svc = new GatewayAuditQueryService(db);
        var first = await svc.SearchAsync(new GatewayAuditSearchRequest { PageSize = 10 }, CancellationToken.None);
        Assert.Equal(10, first.Items.Count);
        Assert.NotNull(first.NextCursor);

        var second = await svc.SearchAsync(new GatewayAuditSearchRequest
        {
            PageSize = 10,
            Cursor = first.NextCursor
        }, CancellationToken.None);

        Assert.Equal(10, second.Items.Count);
        var firstIds = first.Items.Select(x => x.Id).ToHashSet();
        Assert.All(second.Items, x => Assert.DoesNotContain(x.Id, firstIds));
    }

    [Fact]
    public async Task PageSize_clamps_to_gateway_max()
    {
        await using var db = CreateDb();
        for (var i = 0; i < 250; i++)
            db.AuditEntries.Add(CreateEntry($"p-{i}", 200, DateTimeOffset.UtcNow.AddSeconds(-i)));
        await db.SaveChangesAsync();

        var svc = new GatewayAuditQueryService(db);
        var result = await svc.SearchAsync(new GatewayAuditSearchRequest { PageSize = 9999 }, CancellationToken.None);

        Assert.Equal(SearchFilterBase.GatewayAuditMaxPageSize, result.PageSize);
        Assert.Equal(SearchFilterBase.GatewayAuditMaxPageSize, result.Items.Count);
    }

    [Fact]
    public async Task Filters_by_status_code()
    {
        await using var db = CreateDb();
        db.AuditEntries.AddRange(
            CreateEntry("a", 403),
            CreateEntry("b", 403),
            CreateEntry("c", 401));
        await db.SaveChangesAsync();

        var svc = new GatewayAuditQueryService(db);
        var result = await svc.SearchAsync(new GatewayAuditSearchRequest { StatusCode = 403 }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, x => Assert.Equal(403, x.StatusCode));
    }

    [Fact]
    public async Task Filters_by_correlation_id()
    {
        await using var db = CreateDb();
        db.AuditEntries.AddRange(
            CreateEntry("corr-target-1", 502),
            CreateEntry("corr-target-2", 403),
            CreateEntry("corr-other", 401));
        await db.SaveChangesAsync();

        var svc = new GatewayAuditQueryService(db);
        var result = await svc.SearchAsync(new GatewayAuditSearchRequest { CorrelationId = "target" }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, x => Assert.Contains("target", x.CorrelationId, StringComparison.OrdinalIgnoreCase));
    }

    private static GatewayAuditEntry CreateEntry(string corr, int code, DateTimeOffset? at = null) => new()
    {
        OccurredAtUtc = at ?? DateTimeOffset.UtcNow,
        CorrelationId = corr,
        Method = "GET",
        Path = "/api/hr-core/test",
        QueryString = "",
        StatusCode = code,
        ElapsedMs = 12,
        Decision = code == 403 ? "forbidden" : code == 401 ? "challenge" : "allowed"
    };

    private static GatewayAuditDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<GatewayAuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new GatewayAuditDbContext(options);
    }
}
