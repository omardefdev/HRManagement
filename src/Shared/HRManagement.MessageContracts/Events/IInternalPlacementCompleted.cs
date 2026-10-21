namespace HRManagement.MessageContracts.Events;

/// <summary>HCM-007: internal mobility completed after recruitment hire closure.</summary>
public interface IInternalPlacementCompleted
{
    Guid EmployeeId { get; }
    Guid RecruitmentApplicationId { get; }
    Guid? RequisitionId { get; }
    Guid? FundedPositionId { get; }
    Guid? FromOrgUnitId { get; }
    Guid? ToOrgUnitId { get; }
    Guid? TenantId => null;
    string? CorrelationId { get; }
    int SchemaVersion => 1;
}
