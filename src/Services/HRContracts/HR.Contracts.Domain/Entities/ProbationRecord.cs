using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "سجل فترة الاختبار")]
public class ProbationRecord : AuditableEntity
{
    [Display(Name = "العقد")]
    [ForeignKey(nameof(Contract))]
    public Guid ContractId { get; set; }

    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تاريخ البداية")]
    public DateOnly StartDate { get; set; }

    [Display(Name = "تاريخ النهاية")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "النتيجة")]
    public ProbationResult Result { get; set; }

    [Display(Name = "صاحب القرار")]
    public Guid? DecisionBy { get; set; }

    [Display(Name = "تاريخ القرار")]
    public DateOnly? DecisionDate { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public Contract Contract { get; set; } = null!;
}
