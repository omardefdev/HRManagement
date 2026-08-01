namespace Payroll.Application.Abstractions;



/// <summary>STR-002: payroll release gate against the legal control catalog.</summary>

public interface IPayrollLegalControlGuard

{

    /// <summary>Returns error when critical payroll/tax controls are not releaseable.</summary>

    string? ValidatePayrollRelease();



    /// <summary>False for Blocked-Legal capabilities (e.g. MoF certified brackets).</summary>

    bool IsCapabilityAllowed(string requirementId);

}

