using HRManagement.SharedKernel.SeparationOfDuties;

namespace Payroll.Application.Options;

/// <summary>
/// PLAN-58 (D-PAY-5): the double approval. A run whose net is above <see cref="DualApprovalThreshold"/> needs two different holders of
/// <c>Payroll.Approve</c>; at or below it, one. An agency's setting (<c>Payroll:Approval:DualApprovalThreshold</c>).
/// </summary>
public sealed class PayrollApprovalOptions
{
    public const string SectionName = "Payroll:Approval";

    public decimal DualApprovalThreshold { get; set; } = SodMatrixDefaults.DefaultPayrollThreshold;
}

/// <summary>
/// PLAN-58 (D-PAY-3): the independent review before the posting. <see cref="Required"/> (true by default) makes the posting wait for a
/// review record an auditor opened and another auditor closed; an agency that turns it off by its own decision gets a line in the run's
/// log at each posting. <see cref="ReviewerRoles"/> (comma-separated, <c>auditor</c> by default) are the roles of
/// <c>Payroll.AuditReview</c>; the shared policy reads the same key.
/// </summary>
public sealed class PayrollAuditOptions
{
    public const string SectionName = "Payroll:Audit";

    public bool Required { get; set; } = true;

    public string ReviewerRoles { get; set; } = "auditor";
}

/// <summary>PLAN-58 (D-PAY-4): what a service-regime blocker of HR.Core holds back.</summary>
public enum PayrollBlockerMode
{
    /// <summary>The employee: his line is calculated and his order held (<c>REGIME-BLOCKER</c>); the others are paid. The default.</summary>
    HoldEmployee = 0,

    /// <summary>The whole run (the behaviour before PLAN-58): the calculation is refused before the run moves.</summary>
    BlockRun = 1,
}

/// <summary>PLAN-58 (D-PAY-4): <c>Payroll:Blockers:Mode</c>, an agency's setting.</summary>
public sealed class PayrollBlockerOptions
{
    public const string SectionName = "Payroll:Blockers";

    public PayrollBlockerMode Mode { get; set; } = PayrollBlockerMode.HoldEmployee;
}

/// <summary>
/// PLAN-58 (D-PAY-20): a token that carries two conflicting payroll roles passes no financial act. Turned off only by a documented
/// decision; every pass of a conflicting holder is still audited then.
/// </summary>
public sealed class PayrollSodOptions
{
    public const string SectionName = "Payroll:Sod";

    public bool RejectConflictingRoleHolders { get; set; } = true;
}

/// <summary>PLAN-58 (§3): a four-eyes request not decided within <see cref="ExpiryHours"/> expires with no effect.</summary>
public sealed class PayrollDualControlOptions
{
    public const string SectionName = "Payroll:DualControl";

    public int ExpiryHours { get; set; } = 72;
}

/// <summary>
/// PLAN-58 (D-PAY-5): the separation of duties of payroll, with the double-approval threshold taken from the agency's setting rather than
/// the matrix's default. Every other rule and conflicting pair is the matrix's (<see cref="SodMatrixDefaults"/>).
/// </summary>
public static class PayrollSeparationOfDuties
{
    public const string RunEntityType = "PayrollRun";

    public static SeparationOfDutiesService Create(decimal dualApprovalThreshold)
    {
        var rules = SodMatrixDefaults.CreateDefaultRules().ToList();
        foreach (var rule in rules.Where(r => string.Equals(r.EntityType, RunEntityType, StringComparison.OrdinalIgnoreCase)))
            rule.FinancialThreshold = dualApprovalThreshold;
        return new SeparationOfDutiesService(rules, SodMatrixDefaults.CreateDefaultConflicts());
    }
}
