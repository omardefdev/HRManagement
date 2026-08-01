using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

[Display(Name = "الأثر المالي للنقل")]
public class TransferFinancialImpact : BaseEntity
{
    [Required]
    [Display(Name = "طلب النقل")]
    public Guid TransferRequestId { get; set; }

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "فرق الراتب")]
    public decimal SalaryChange { get; set; }

    [Display(Name = "فرق البدلات")]
    public decimal AllowanceChange { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "تاريخ الحساب")]
    public DateTime CalculatedAt { get; set; }

    public TransferRequest TransferRequest { get; set; } = null!;
}
