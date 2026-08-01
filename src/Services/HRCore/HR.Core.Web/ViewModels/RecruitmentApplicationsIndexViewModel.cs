using HR.Core.Domain.Entities;

namespace HR.Core.Web.ViewModels;

public class RecruitmentApplicationsIndexViewModel
{
    public IReadOnlyList<RecruitmentApplication> Applications { get; set; } = [];
    public RecruitmentApplicationCreateViewModel CreateModel { get; set; } = new();
    public bool ShowCreateModal { get; set; }
}
