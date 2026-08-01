namespace Payroll.Domain.Enums;

public enum EosStatus
{
    Draft,
    Approved,
    Paid,
    /// <summary>FUN-002: draft voided by separation compensate.</summary>
    Cancelled
}
