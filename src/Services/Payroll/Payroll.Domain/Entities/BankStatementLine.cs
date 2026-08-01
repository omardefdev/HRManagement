using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

/// <summary>Imported bank statement row for reconciliation against <see cref="PaymentOrder"/>.</summary>
public class BankStatementLine : BaseEntity
{
    [Required]
    [MaxLength(128)]
    public string StatementReference { get; set; } = null!;

    [MaxLength(128)]
    public string? BankReference { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(34)]
    public string? BeneficiaryIban { get; set; }

    public DateTime ValueDate { get; set; }

    public Guid? MatchedPaymentOrderId { get; set; }

    public DateTime? MatchedAt { get; set; }
}
