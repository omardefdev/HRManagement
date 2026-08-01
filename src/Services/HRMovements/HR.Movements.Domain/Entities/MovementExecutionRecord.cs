using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

[Display(Name = "سجل تنفيذ النقل")]
public class MovementExecutionRecord : BaseEntity
{
    [Display(Name = "طلب النقل")]
    public Guid? TransferRequestId { get; set; }

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "وقت التنفيذ")]
    public DateTime ExecutedAt { get; set; }

    [Display(Name = "نفذ بواسطة")]
    public Guid? ExecutedBy { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "طلب الانتداب")]
    public Guid? SecondmentRequestId { get; set; }

    [Display(Name = "طلب الإعارة")]
    public Guid? LoanAssignmentRequestId { get; set; }

    public TransferRequest? TransferRequest { get; set; }
    public SecondmentRequest? SecondmentRequest { get; set; }
    public LoanAssignmentRequest? LoanAssignmentRequest { get; set; }
}
