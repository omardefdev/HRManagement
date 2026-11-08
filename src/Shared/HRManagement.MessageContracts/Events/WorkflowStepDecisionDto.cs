namespace HRManagement.MessageContracts.Events;

/// <summary>PLAN-46: one decision on an approval. <paramref name="Action"/> is Approved, Rejected or Withdrawn.</summary>
public sealed record WorkflowStepDecisionDto(int Level, string RoleRequired, Guid? UserId, string Action, DateTime At);
