namespace HRManagement.MessageContracts.Events;

public interface IPromotionExecuted
{
    Guid PromotionRequestId { get; }
    Guid EmployeeId { get; }
    string? SourceLegalRegimeId { get; }
    int FromGrade { get; }
    int ToGrade { get; }
    int FromStep { get; }
    int ToStep { get; }
    string TransitionDecisionRef { get; }
    DateTime EffectiveDate { get; }
    Guid CorrelationId { get; }
    string SourceSystem { get; }

    Guid? TenantId => null;

    int SchemaVersion => 1;
}
