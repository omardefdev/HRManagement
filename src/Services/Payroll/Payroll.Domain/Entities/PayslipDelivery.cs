using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "توزيع القسيمة")]
public class PayslipDelivery : BaseEntity
{
    [Required]
    [ForeignKey(nameof(SalaryLine))]
    public Guid SalaryLineId { get; set; }

    [Display(Name = "الحالة")]
    public PayslipDeliveryStatus Status { get; set; } = PayslipDeliveryStatus.Generated;

    [Display(Name = "تاريخ الإرسال")]
    public DateTime? SentAt { get; set; }

    [Display(Name = "تاريخ المشاهدة")]
    public DateTime? ViewedAt { get; set; }

    public SalaryLine SalaryLine { get; set; } = null!;
}
