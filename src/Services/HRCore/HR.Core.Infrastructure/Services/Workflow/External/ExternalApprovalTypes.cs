using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.Services.Workflow.External;

/// <summary>How the owner hears the outcome: the custody events now, the general event in PLAN-46.</summary>
public enum ExternalOutcomeEvent
{
    AssetLegacy,
    Generic
}

/// <param name="AudienceRoles">Roles from outside HR.Core that reach the inbox for this type.</param>
public sealed record ExternalApprovalType(
    string EntityType,
    string OwnerClientId,
    IReadOnlyList<string> AllowedWorkflowCodes,
    string DefaultWorkflowCode,
    ExternalOutcomeEvent OutcomeEvent,
    IReadOnlyList<string> AudienceRoles,
    string DetailsUrlPrefix);

/// <summary>
/// PLAN-43 (G-WF-05): the entity types started from outside HR.Core, each with the one service account that owns it. A type here
/// starts only through <see cref="ExternalApprovalService"/>.
/// </summary>
public static class ExternalApprovalTypes
{
    public static readonly IReadOnlyList<ExternalApprovalType> All =
    [
        new(
            WorkflowAssetOutcomes.AssignmentEntityType,
            ServiceClientIds.AdminLogistics,
            ["ASSET-ASSIGN-APPROVAL"],
            "ASSET-ASSIGN-APPROVAL",
            ExternalOutcomeEvent.AssetLegacy,
            [HrManagementRoles.AdminLogistics],
            "/ui/admin-logistics/"),
        new(
            WorkflowAssetOutcomes.LossEntityType,
            ServiceClientIds.AdminLogistics,
            ["ASSET-LOSS-APPROVAL"],
            "ASSET-LOSS-APPROVAL",
            ExternalOutcomeEvent.AssetLegacy,
            [HrManagementRoles.AdminLogistics],
            "/ui/admin-logistics/")
    ];

    public static ExternalApprovalType? Find(string? entityType) =>
        All.FirstOrDefault(t => string.Equals(t.EntityType, entityType, StringComparison.OrdinalIgnoreCase));

    public static bool IsRegistered(string? entityType) => Find(entityType) is not null;
}
