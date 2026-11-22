namespace HRManagement.MessageContracts.Events;

/// <summary>
/// PLAN-53: a salary or grade change was requested for an employee and waits for a second person's approval. It carries no amount:
/// a reader of the bus learns that a change is pending, never its value.
/// </summary>
public interface IPayProfileChangeSubmitted
{
    Guid RequestId { get; }
    Guid EmployeeId { get; }

    /// <summary><c>BaseSalary</c>, <c>PayGrade</c> or <c>BaseSalaryAndGrade</c>.</summary>
    string ChangeKind { get; }

    /// <summary><c>Manual</c> or <c>Api</c>.</summary>
    string Source { get; }

    DateOnly EffectiveFrom { get; }
    Guid RequestedBy { get; }
    DateTime OccurredAt { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
