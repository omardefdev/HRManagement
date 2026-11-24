namespace Payroll.Domain.Constants;

/// <summary>PLAN-53: the names of the filtered unique indexes of the change requests, so a unique violation is told apart by name.</summary>
public static class PayrollChangeIndexNames
{
    /// <summary>D-53-3: one open salary change request (pending or scheduled) per employee and agency.</summary>
    public const string OneOpenPayProfileChange = "IX_PayProfileChangeRequests_OneOpen";

    /// <summary>One open reference change request per scale or grade.</summary>
    public const string OneOpenReferenceChange = "IX_PayrollReferenceChangeRequests_OneOpen";

    /// <summary>
    /// PLAN-55: one account in force and one account waiting for verification per employee and agency. One index over the status, so
    /// that a verification's two updates (the account in force retired, its replacement in force) are ordered by their values.
    /// </summary>
    public const string OneActiveOnePendingBankAccount = "IX_EmployeeBankAccounts_OneActiveOnePending";
}
