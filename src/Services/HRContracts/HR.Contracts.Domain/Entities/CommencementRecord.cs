using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "إذن المباشرة")]
public class CommencementRecord : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "العقد")]
    [ForeignKey(nameof(Contract))]
    public Guid? ContractId { get; set; }

    [Display(Name = "تاريخ البداية")]
    [Required]
    public DateOnly StartDate { get; set; }

    [Display(Name = "المُبلغ")]
    public Guid? NotifiedBy { get; set; }

    [Display(Name = "تاريخ الإبلاغ")]
    public DateTime? NotificationDate { get; set; }

    public Contract? Contract { get; set; }
}
