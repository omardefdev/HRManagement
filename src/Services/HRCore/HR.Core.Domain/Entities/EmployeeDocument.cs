using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Entities;

public class EmployeeDocument : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع المستند")]
    public DocumentType DocType { get; set; }

    [Display(Name = "عنوان المستند")]
    [Required(ErrorMessage = "عنوان المستند مطلوب")]
    [StringLength(200)]
    public string DocTitle { get; set; } = string.Empty;

    [Display(Name = "معرف الملف")]
    public Guid? FileId { get; set; }

    [Display(Name = "جهة الإصدار")]
    [StringLength(200)]
    public string? IssuedBy { get; set; }

    [Display(Name = "تاريخ الإصدار")]
    [DataType(DataType.Date)]
    public DateTime? IssueDate { get; set; }

    [Display(Name = "تاريخ الانتهاء")]
    [DataType(DataType.Date)]
    public DateTime? ExpiryDate { get; set; }

    [Display(Name = "موثق")]
    public bool Verified { get; set; }

    public virtual Employee? Employee { get; set; }
}
