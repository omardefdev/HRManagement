using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>Atomic treasury instruction with idempotency key (SYR-011 / EXT-003).</summary>
public class PaymentOrder : BaseEntity
{
    [Required]
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = null!;

    public Guid PayrollRunId { get; set; }
    public Guid PaymentBatchId { get; set; }
    public Guid SalaryLineId { get; set; }
    public Guid EmployeeId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string CurrencyCode { get; set; } = "SYP";

    [MaxLength(64)]
    public string Channel { get; set; } = "treasury-file";

    [MaxLength(34)]
    public string? BeneficiaryIban { get; set; }

    public PaymentOrderStatus Status { get; set; } = PaymentOrderStatus.Draft;

    [MaxLength(128)]
    public string? ExternalReference { get; set; }

    [MaxLength(128)]
    public string? SettlementReference { get; set; }

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public DateTime? RejectedAt { get; set; }
}
