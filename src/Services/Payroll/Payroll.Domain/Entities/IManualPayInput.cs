using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-54 (§4, D-PAY-6): what an allowance, a deduction and an adjustment share once a person may enter one by hand: it counts in a
/// calculation only when <see cref="ApprovalStatus"/> is approved, and a manual one is approved by a second person.
/// </summary>
public interface IManualPayInput
{
    Guid Id { get; }

    Guid EmployeeId { get; }

    ManualInputApprovalStatus ApprovalStatus { get; set; }

    ManualInputOrigin Origin { get; set; }

    /// <summary>Who entered a manual input (the maker). Null on a system row.</summary>
    Guid? CreatedBy { get; set; }

    Guid? ApprovedBy { get; set; }

    DateTime? ApprovedAt { get; set; }

    string? DecisionNote { get; set; }
}

public static class ManualPayInputRules
{
    public const int DecisionNoteMaxLength = 500;
    public const int ReasonMaxLength = 500;
}
