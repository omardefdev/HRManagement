using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Entities;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.ViewModels;

public class ApprovalListViewModel
{
    public PaginatedList<ApprovalRequest> Requests { get; set; } = null!;
    public SearchFilterBase Filter { get; set; } = new();
    public string? StatusFilter { get; set; }
    public string? EntityTypeFilter { get; set; }
}

public class ApprovalReviewViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "نوع الكيان")]
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    [Display(Name = "وصف الطلب")]
    public string? Description { get; set; }

    [Display(Name = "مقدّم الطلب")]
    public string? RequestedByName { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedAt { get; set; }

    [Display(Name = "الحالة")]
    public ApprovalStatus Status { get; set; }

    [Display(Name = "المستوى الحالي")]
    public int CurrentLevel { get; set; }

    public List<ApprovalStep> Steps { get; set; } = new();

    [Display(Name = "ملاحظات المراجعة")]
    public string? ReviewComments { get; set; }
}
