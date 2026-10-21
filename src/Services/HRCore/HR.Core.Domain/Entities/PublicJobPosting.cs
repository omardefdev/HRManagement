using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "إعلان وظيفي عام")]
public class PublicJobPosting : AuditableEntity
{
    [Required(ErrorMessage = "عنوان الوظيفة مطلوب")]
    [Display(Name = "عنوان الوظيفة")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الجهة")]
    [StringLength(200)]
    public string? Entity { get; set; }

    [Display(Name = "مكان العمل")]
    [StringLength(200)]
    public string? Location { get; set; }

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    [Display(Name = "الشروط والمؤهلات")]
    public string? Requirements { get; set; }

    [Display(Name = "تاريخ الإغلاق")]
    public DateOnly? ClosingDate { get; set; }

    [Display(Name = "منشور")]
    public bool IsPublished { get; set; } = true;

    [Display(Name = "طلب الشاغر")]
    public Guid? RequisitionId { get; set; }

    public virtual RecruitmentRequisition? Requisition { get; set; }

    public virtual ICollection<CitizenApplication> Applications { get; set; } = new List<CitizenApplication>();
}
