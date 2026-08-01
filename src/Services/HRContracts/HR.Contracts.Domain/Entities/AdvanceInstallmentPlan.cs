using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "خطة أقساط السلفة")]
public class AdvanceInstallmentPlan : BaseEntity
{
    [Display(Name = "طلب السلفة")]
    [Required]
    public Guid AdvanceRequestId { get; set; }

    [Display(Name = "المبلغ الإجمالي")]
    public decimal TotalAmount { get; set; }

    [Display(Name = "القسط الشهري")]
    public decimal MonthlyInstallment { get; set; }

    [Display(Name = "عدد الأقساط")]
    public int NumberOfInstallments { get; set; }

    [Display(Name = "شهر البداية")]
    public DateTime StartMonth { get; set; }

    [Display(Name = "المبلغ المتبقي")]
    public decimal RemainingAmount { get; set; }

    public virtual AdvanceRequest? AdvanceRequest { get; set; }
}
