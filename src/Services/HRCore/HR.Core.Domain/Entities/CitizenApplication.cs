using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "طلب توظيف مواطن")]
public class CitizenApplication : AuditableEntity
{
    [Display(Name = "الإعلان الوظيفي")]
    public Guid PublicJobPostingId { get; set; }

    [Required]
    [Display(Name = "الرقم المرجعي")]
    [StringLength(40)]
    public string ReferenceNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "الاسم الكامل مطلوب")]
    [Display(Name = "الاسم الكامل")]
    [StringLength(300)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "الرقم الوطني مطلوب")]
    [Display(Name = "الرقم الوطني")]
    [StringLength(20)]
    public string NationalId { get; set; } = string.Empty;

    [Display(Name = "رقم الهاتف")]
    [StringLength(20)]
    public string? Phone { get; set; }

    [Display(Name = "البريد الإلكتروني")]
    [StringLength(150)]
    public string? Email { get; set; }

    [Display(Name = "نبذة/مسوّغات")]
    public string? CoverNote { get; set; }

    [Display(Name = "تاريخ التقديم")]
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "الحالة")]
    public CitizenApplicationStatus Status { get; set; } = CitizenApplicationStatus.Received;

    public virtual PublicJobPosting? PublicJobPosting { get; set; }
}
