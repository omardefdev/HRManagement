using HRManagement.SharedKernel.ViewModels;

namespace HRManagement.UnitTests;

public class Perf002PagingTests
{
    [Fact]
    public void NormalizePaging_clamps_oversized_page_size()
    {
        var filter = new SearchFilterBase { Page = 0, PageSize = 9999 };
        filter.NormalizePaging();

        Assert.Equal(1, filter.Page);
        Assert.Equal(SearchFilterBase.MaxPageSize, filter.PageSize);
    }

    [Fact]
    public void NormalizePaging_uses_default_when_page_size_invalid()
    {
        var filter = new SearchFilterBase { Page = 2, PageSize = 0 };
        filter.NormalizePaging();

        Assert.Equal(2, filter.Page);
        Assert.Equal(SearchFilterBase.DefaultPageSize, filter.PageSize);
    }

    [Fact]
    public void ClampPageSize_respects_custom_max()
    {
        Assert.Equal(50, SearchFilterBase.ClampPageSize(999, SearchFilterBase.LookupMaxPageSize));
        Assert.Equal(200, SearchFilterBase.ClampPageSize(500, SearchFilterBase.GatewayAuditMaxPageSize));
    }

    [Fact]
    public void KeysetCursor_roundtrips_long_id()
    {
        var at = DateTimeOffset.UtcNow;
        var encoded = KeysetCursor.Encode(at, 42L);
        Assert.True(KeysetCursor.TryDecodeLong(encoded, out var decodedAt, out var id));
        Assert.Equal(42L, id);
        Assert.Equal(at.UtcTicks, decodedAt.UtcTicks);
    }

    [Fact]
    public void Export_max_rows_constant_is_10000()
    {
        Assert.Equal(10_000, SearchFilterBase.MaxExportRows);
    }
}
