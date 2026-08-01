namespace Payroll.Domain.Enums;

public enum PayrollRunStatus
{
    Draft = 0,
    Calculated = 1,
    Posted = 2,
    UnderReview = 3,
    Approved = 4,
    Reversed = 5,
    /// <summary>PAY-003: calculation in progress (resumable).</summary>
    Calculating = 6,
    /// <summary>PAY-003: totals validation in progress.</summary>
    Validating = 7,
    /// <summary>PAY-003: job cancelled before post.</summary>
    Cancelled = 8,

    /// <summary>Legacy alias for <see cref="Draft"/>.</summary>
    Pending = Draft,

    /// <summary>Legacy alias for <see cref="Calculated"/>.</summary>
    Processed = Calculated,
}
