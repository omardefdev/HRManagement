namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>Periodic SoD conflict finding for compliance review (IAM-003).</summary>
public record SodViolationReport(
    Guid UserId,
    string UserName,
    string RoleA,
    string RoleB,
    string ConflictReason,
    DateTime DetectedAt);

/// <summary>Snapshot of a user's assigned roles used by SoD reporting.</summary>
public record UserRoleSnapshot(Guid UserId, string UserName, IReadOnlyList<string> Roles);
