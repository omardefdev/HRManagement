using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "سجل ملف الراتب")]
public class EmployeePayProfileHistory : BaseEntity
{
    [Required]
    public Guid EmployeeId { get; set; }

    public Guid? PayGradeId { get; set; }

    public decimal BaseSalary { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    [MaxLength(500)]
    public string? ChangeReason { get; set; }

    [ForeignKey(nameof(SourceProfile))]
    public Guid? SourceProfileId { get; set; }

    public EmployeePayProfile? SourceProfile { get; set; }
}
