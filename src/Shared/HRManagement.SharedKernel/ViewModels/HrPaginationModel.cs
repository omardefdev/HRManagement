namespace HRManagement.SharedKernel.ViewModels;

public sealed class HrPaginationModel
{
    public int PageIndex { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public bool HasPreviousPage { get; init; }
    public bool HasNextPage { get; init; }

    public static HrPaginationModel From<T>(PaginatedList<T> source) => new()
    {
        PageIndex = source.PageIndex,
        TotalPages = source.TotalPages,
        TotalCount = source.TotalCount,
        HasPreviousPage = source.HasPreviousPage,
        HasNextPage = source.HasNextPage
    };
}
