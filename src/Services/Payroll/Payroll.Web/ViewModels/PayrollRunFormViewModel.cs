using System.ComponentModel.DataAnnotations;

namespace Payroll.Web.ViewModels;

public sealed class PayrollRunFormViewModel
{
    [Display(Name = "بداية الفترة")]
    [Required]
    [DataType(DataType.Date)]
    public DateTime PeriodStart { get; set; }

    [Display(Name = "نهاية الفترة")]
    [Required]
    [DataType(DataType.Date)]
    public DateTime PeriodEnd { get; set; }

    [Display(Name = "تاريخ التشغيل")]
    [Required]
    [DataType(DataType.Date)]
    public DateTime RunDate { get; set; }
}
