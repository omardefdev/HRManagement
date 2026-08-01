using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class PerformanceEvaluation : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "السنة")]
    [Required(ErrorMessage = "السنة مطلوبة")]
    public int Year { get; set; }

    [Display(Name = "المُقيّم")]
    public Guid? EvaluatorId { get; set; }

    [Display(Name = "الدرجة")]
    public decimal Score { get; set; }

    [Display(Name = "ملخص التقييم")]
    public string? Summary { get; set; }

    [Display(Name = "الحالة")]
    [StringLength(20)]
    public string Status { get; set; } = "draft";

    public virtual Employee? Employee { get; set; }
}
