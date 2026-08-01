using HRManagement.SharedKernel.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

/// <summary>PERF-002: EF helpers for offset pagination with normalized page size.</summary>
public static class PaginatedListExtensions
{
    public static async Task<PaginatedList<T>> ToPaginatedListAsync<T>(
        this IQueryable<T> source,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        int maxPageSize = SearchFilterBase.MaxPageSize)
    {
        page = SearchFilterBase.ClampPage(page);
        pageSize = SearchFilterBase.ClampPageSize(pageSize, maxPageSize);

        var count = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<T>(items, count, page, pageSize);
    }

    public static async Task<PaginatedList<T>> ToPaginatedListAsync<T>(
        this IQueryable<T> source,
        SearchFilterBase filter,
        CancellationToken cancellationToken = default,
        int maxPageSize = SearchFilterBase.MaxPageSize)
    {
        filter.NormalizePaging(maxPageSize);
        return await source.ToPaginatedListAsync(filter.Page, filter.PageSize, cancellationToken, maxPageSize);
    }
}

/// <summary>PERF-002: shared export row-cap checks (sync HTTP only; heavy jobs = PERF-004).</summary>
public static class ExportQueryLimits
{
    public const string OverLimitArabicMessage =
        "حجم التصدير يتجاوز الحد المسموح (10,000 صف). أعد الطلب مع async=true لجدولة تصدير خلفية (PERF-004) أو ضيّق عوامل التصفية.";

    public static async Task<(bool Allowed, int Count)> EnsureWithinLimitAsync<T>(
        IQueryable<T> query,
        int maxRows = SearchFilterBase.MaxExportRows,
        CancellationToken cancellationToken = default)
    {
        var count = await query.CountAsync(cancellationToken);
        return (count <= maxRows, count);
    }
}
