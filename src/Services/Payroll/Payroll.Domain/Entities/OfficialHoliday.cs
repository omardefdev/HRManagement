using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "عطلة رسمية")]
public class OfficialHoliday : BaseEntity
{
    [Display(Name = "التاريخ")]
    public DateOnly Date { get; set; }

    [Required, MaxLength(200)]
    [Display(Name = "الاسم")]
    public string NameAr { get; set; } = null!;

    [Display(Name = "متكررة سنوياً")]
    public bool IsRecurring { get; set; }

    [Display(Name = "إعداد الرواتب")]
    public Guid? PayrollConfigurationId { get; set; }

    public virtual PayrollConfiguration? PayrollConfiguration { get; set; }
}
