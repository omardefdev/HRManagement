namespace HRManagement.MessageContracts.Events;

/// <summary>
/// PLAN-53: a salary or grade change reached the pay profile: a new version exists from <see cref="EffectiveFrom"/> and the previous one
/// closed the day before. Published for every application, of an approved request or of an automatic source.
/// </summary>
public interface IPayProfileChangeApplied
{
    Guid RequestId { get; }
    Guid EmployeeId { get; }
    Guid NewProfileId { get; }
    Guid? PreviousProfileId { get; }
    DateOnly EffectiveFrom { get; }

    /// <summary><c>Manual</c>, <c>Api</c>, <c>ContractActivated</c>, <c>Promotion</c>, <c>Transfer</c> or <c>Correction</c>.</summary>
    string Source { get; }

    string? SourceReference { get; }
    string? PayGradeCode { get; }
    decimal BaseSalary { get; }
    decimal? PreviousBaseSalary { get; }
    DateTime OccurredAt { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
