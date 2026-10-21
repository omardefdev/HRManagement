using HRManagement.SharedKernel.ViewModels;

namespace Admin.Logistics.Web.Services;

public abstract class AdminLogisticsPagedFilter
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = SearchFilterBase.DefaultPageSize;

    public (int Page, int PageSize) NormalizePaging()
    {
        var page = SearchFilterBase.ClampPage(Page);
        var pageSize = SearchFilterBase.ClampPageSize(PageSize);
        return (page, pageSize);
    }
}
