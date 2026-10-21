using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Payroll;

public enum ArrearsStatus
{
    [Display(Name = "مفتوح")]
    Open,
    [Display(Name = "مسدّد جزئياً")]
    PartiallySettled,
    [Display(Name = "مسدّد")]
    Settled,
    [Display(Name = "شُطب")]
    WrittenOff
}

/// <summary>
/// Tracks the difference between entitled and actually disbursed payroll amounts
/// when treasury funding is insufficient for full payment. Invariant:
/// <c>DisbursedAmount + ArrearsBalance == EntitledAmount</c>.
/// </summary>
public class ArrearsLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Guid PayrollRunId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public decimal EntitledAmount { get; set; }
    public decimal DisbursedAmount { get; set; }
    public decimal ArrearsBalance => EntitledAmount - DisbursedAmount;

    public string CurrencyCode { get; set; } = "SYP";
    public string CurrencyEra { get; set; } = "new";

    public string? Reason { get; set; }
    public ArrearsStatus Status { get; set; } = ArrearsStatus.Open;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SettledAt { get; set; }

    /// <summary>
    /// Apply a catch-up payment. Returns false if the payment would exceed the
    /// remaining arrears balance (prevents double-payment).
    /// </summary>
    public bool ApplyCatchUp(decimal amount)
    {
        if (amount <= 0 || amount > ArrearsBalance)
            return false;

        DisbursedAmount += amount;
        if (ArrearsBalance == 0)
        {
            Status = ArrearsStatus.Settled;
            SettledAt = DateTimeOffset.UtcNow;
        }
        else
        {
            Status = ArrearsStatus.PartiallySettled;
        }
        return true;
    }
}
