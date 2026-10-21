using HR.Core.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Core.Web.ViewModels;

public class RecruitmentApplicationsIndexViewModel
{
    public IReadOnlyList<RecruitmentApplication> Applications { get; set; } = [];
    public RecruitmentApplicationCreateViewModel CreateModel { get; set; } = new();
    public bool ShowCreateModal { get; set; }
    public RecruitmentApplicationsFilter Filter { get; set; } = new();
    public int TotalCount { get; set; }
    public SelectList? RequisitionFilterOptions { get; set; }
}

public class RecruitmentApplicationsFilter
{
    public string? SearchTerm { get; set; }
    public string? Status { get; set; }
    public Guid? RequisitionId { get; set; }
    public bool LegacyOnly { get; set; }
    public bool? HasOffer { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public void Normalize()
    {
        Page = Page < 1 ? 1 : Math.Min(Page, 10_000);
        PageSize = PageSize is < 5 or > 100 ? 20 : PageSize;
        SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm.Trim();
    }
}
