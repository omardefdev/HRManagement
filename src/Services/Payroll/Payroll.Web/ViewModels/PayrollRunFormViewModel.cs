using System.ComponentModel.DataAnnotations;

namespace Payroll.Web.ViewModels;

public sealed class PayrollRunFormViewModel
{
    [Display(Name = "بداية الفترة")]
    [Required]
    public DateTime PeriodStart { get; set; }

    [Display(Name = "نهاية الفترة")]
    [Required]
    public DateTime PeriodEnd { get; set; }

    [Display(Name = "تاريخ التشغيل")]
    [Required]
    public DateTime RunDate { get; set; }
}
