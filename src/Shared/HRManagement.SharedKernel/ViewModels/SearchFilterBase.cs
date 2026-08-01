namespace HRManagement.SharedKernel.ViewModels;

/// <summary>PERF-002: shared paging defaults and hard caps for list endpoints.</summary>
public class SearchFilterBase
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int LookupMaxPageSize = 50;
    public const int GatewayAuditMaxPageSize = 200;
    public const int MaxExportRows = 10_000;

    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;
    public string? SortBy { get; set; }
    public string SortDirection { get; set; } = "asc";

    /// <summary>Clamp page/pageSize to safe bounds (PERF-002).</summary>
    public void NormalizePaging(int maxPageSize = MaxPageSize)
    {
        Page = Page < 1 ? 1 : Page;
        if (PageSize < 1)
            PageSize = DefaultPageSize;
        PageSize = Math.Clamp(PageSize, 1, maxPageSize);
    }

    public static int ClampPageSize(int pageSize, int maxPageSize = MaxPageSize)
    {
        if (pageSize < 1) return DefaultPageSize;
        return Math.Clamp(pageSize, 1, maxPageSize);
    }

    public static int ClampPage(int page) => page < 1 ? 1 : page;
}
