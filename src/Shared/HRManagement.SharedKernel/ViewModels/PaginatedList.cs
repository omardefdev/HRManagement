namespace HRManagement.SharedKernel.ViewModels;

public class PaginatedList<T>
{
    public IReadOnlyList<T> Items { get; }
    public int PageIndex { get; }
    public int TotalPages { get; }
    public int TotalCount { get; }
    public int PageSize { get; }

    public PaginatedList(IReadOnlyList<T> items, int count, int pageIndex, int pageSize)
    {
        Items = items;
        TotalCount = count;
        PageSize = pageSize;
        PageIndex = pageIndex;
        TotalPages = pageSize <= 0 ? 0 : (int)Math.Ceiling(count / (double)pageSize);
    }

    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;

    public static PaginatedList<T> Empty(int pageIndex = 1, int pageSize = SearchFilterBase.DefaultPageSize) =>
        new([], 0, pageIndex, pageSize);
}
