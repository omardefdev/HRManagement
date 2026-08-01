using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "العقد")]
public class Contract : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع العقد")]
    [ForeignKey(nameof(ContractType))]
    public Guid ContractTypeId { get; set; }

    [Display(Name = "تاريخ البداية")]
    [Required(ErrorMessage = "تاريخ البداية مطلوب")]
    public DateOnly StartDate { get; set; }

    [Display(Name = "تاريخ النهاية")]
    public DateOnly? EndDate { get; set; }

    [Display(Name = "الحالة")]
    public ContractStatus Status { get; set; }

    [Display(Name = "البنود")]
    public string? Terms { get; set; }

    [Display(Name = "الموقّع")]
    public Guid? SignedBy { get; set; }

    [Display(Name = "تاريخ التوقيع")]
    public DateOnly? SignedDate { get; set; }

    [Display(Name = "فترة الاختبار")]
    public int? ProbationMonths { get; set; }

    public ContractType ContractType { get; set; } = null!;
    public ICollection<ContractSignature> Signatures { get; set; } = new List<ContractSignature>();
}
