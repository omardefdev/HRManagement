using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;

namespace HR.Core.Web.ViewModels;

public class CareerPathCreateViewModel
{
    [Required(ErrorMessage = "عنوان المسار مطلوب")]
    [Display(Name = "عنوان المسار")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الوصف")]
    public string? Description { get; set; }
}

public class ShiftCreateViewModel
{
    [Required(ErrorMessage = "رمز الوردية مطلوب")]
    [Display(Name = "رمز الوردية")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم الوردية مطلوب")]
    [Display(Name = "اسم الوردية")]
    [StringLength(150)]
    public string NameAr { get; set; } = string.Empty;

    [Display(Name = "نوع الوردية")]
    public ShiftType ShiftType { get; set; } = ShiftType.Day;

    [Display(Name = "وقت البدء")]
    public TimeOnly StartTime { get; set; } = new(8, 0);

    [Display(Name = "وقت الانتهاء")]
    public TimeOnly EndTime { get; set; } = new(16, 0);

    [Display(Name = "مفعّلة")]
    public bool IsActive { get; set; } = true;
}

public class ShiftEditViewModel : ShiftCreateViewModel
{
    public Guid Id { get; set; }
}

public class KeyPositionCreateViewModel
{
    [Required(ErrorMessage = "عنوان المنصب مطلوب")]
    [Display(Name = "عنوان المنصب")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "شاغل المنصب الحالي")]
    public Guid? CurrentHolderEmployeeId { get; set; }

    [Display(Name = "درجة الأهمية (1-5)")]
    [Range(1, 5)]
    public int Criticality { get; set; } = 3;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public class KeyPositionEditViewModel : KeyPositionCreateViewModel
{
    public Guid Id { get; set; }
}

public class InternalVacancyCreateViewModel
{
    [Required(ErrorMessage = "عنوان الشاغر مطلوب")]
    [Display(Name = "عنوان الشاغر")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "الوصف")]
    public string? Description { get; set; }

    [Display(Name = "المؤهلات المطلوبة")]
    public string? RequiredQualifications { get; set; }

    [Display(Name = "تاريخ الإغلاق")]
    public DateOnly? ClosingDate { get; set; }

    [Display(Name = "مفتوح للترشّح")]
    public bool IsOpen { get; set; } = true;
}

public class InternalVacancyEditViewModel : InternalVacancyCreateViewModel
{
    public Guid Id { get; set; }
}

public class PublicJobPostingCreateViewModel
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
}

public class PublicJobPostingEditViewModel : PublicJobPostingCreateViewModel
{
    public Guid Id { get; set; }
}

public class SuccessionPlanCreateViewModel
{
    [Display(Name = "المنصب الحرج")]
    [Required]
    public Guid KeyPositionId { get; set; }

    [Display(Name = "السنة")]
    [Range(2000, 2100)]
    public int Year { get; set; } = DateTime.UtcNow.Year;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public class ShiftRosterCreateViewModel
{
    [Required(ErrorMessage = "عنوان الجدول مطلوب")]
    [Display(Name = "عنوان الجدول")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "بداية الفترة")]
    public DateOnly PeriodStart { get; set; }

    [Display(Name = "نهاية الفترة")]
    public DateOnly PeriodEnd { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}
