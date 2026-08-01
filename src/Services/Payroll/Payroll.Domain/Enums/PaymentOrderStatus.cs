namespace Payroll.Domain.Enums;

/// <summary>SYR-011 per-beneficiary payment order lifecycle.</summary>
public enum PaymentOrderStatus
{
    Draft = 0,
    Submitted = 1,
    Acknowledged = 2,
    Rejected = 3,
    Settled = 4,
    PartiallySettled = 5
}
