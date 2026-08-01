using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Core.Web.ViewModels;

public class EmploymentRecordCreateViewModel
{
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }

    [Display(Name = "المسمى الوظيفي")]
    [Required(ErrorMessage = "المسمى الوظيفي مطلوب")]
    [StringLength(200)]
    public string PositionTitle { get; set; } = string.Empty;

    [Display(Name = "كود الوظيفة")]
    [StringLength(50)]
    public string? PositionCode { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "تاريخ البداية")]
    [Required(ErrorMessage = "تاريخ البداية مطلوب")]
    [DataType(DataType.Date)]
    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;

    [Display(Name = "تاريخ النهاية")]
    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }

    [Display(Name = "نوع التوظيف")]
    public EmploymentType EmploymentType { get; set; }

    [Display(Name = "الحالة")]
    [StringLength(20)]
    public string Status { get; set; } = "active";

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public SelectList? OrgUnits { get; set; }
}

public class EmploymentRecordEditViewModel : EmploymentRecordCreateViewModel
{
    public Guid Id { get; set; }
}
