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
    /// <summary>HCM-030 (E6): document completeness per listed application (absent: no documents and none required).</summary>
    public IReadOnlyDictionary<Guid, HR.Core.Web.Services.CandidateDocumentCompleteness> DocumentCompleteness { get; set; } =
        new Dictionary<Guid, HR.Core.Web.Services.CandidateDocumentCompleteness>();
}

public class RecruitmentApplicationsFilter
{
    public string? SearchTerm { get; set; }
    public string? Status { get; set; }
    public Guid? RequisitionId { get; set; }
    public bool LegacyOnly { get; set; }
    /// <summary>HCM-031 (PLAN-31): only the applications of one competition.</summary>
    public Guid? CompetitionId { get; set; }
    public bool? HasOffer { get; set; }
    /// <summary>HCM-030 (D4): only applications with eligibility flags.</summary>
    public bool WithEligibilityFlags { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public void Normalize()
    {
        Page = Page < 1 ? 1 : Math.Min(Page, 10_000);
        PageSize = PageSize is < 5 or > 100 ? 20 : PageSize;
        SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm.Trim();
    }
}
