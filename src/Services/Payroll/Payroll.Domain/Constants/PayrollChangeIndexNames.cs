namespace Payroll.Domain.Constants;

/// <summary>PLAN-53: the names of the filtered unique indexes of the change requests, so a unique violation is told apart by name.</summary>
public static class PayrollChangeIndexNames
{
    /// <summary>D-53-3: one open salary change request (pending or scheduled) per employee and agency.</summary>
    public const string OneOpenPayProfileChange = "IX_PayProfileChangeRequests_OneOpen";

    /// <summary>One open reference change request per scale or grade.</summary>
    public const string OneOpenReferenceChange = "IX_PayrollReferenceChangeRequests_OneOpen";
}
