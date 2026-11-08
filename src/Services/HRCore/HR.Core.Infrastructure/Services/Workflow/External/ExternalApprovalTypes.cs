using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.Services.Workflow.External;

/// <summary>How the owner hears the outcome: the custody events, or the general event of PLAN-46 (<c>IWorkflowOutcomeDecided</c>).</summary>
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
    public const string CardRequestEntityType = "CardRequest";
    public const string DisciplinaryActionEntityType = "DisciplinaryAction";
    public const string SeparationRecordEntityType = "SeparationRecord";

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
            "/ui/admin-logistics/"),
        new(
            CardRequestEntityType,
            ServiceClientIds.IdCard,
            ["ID-CARD-ISSUANCE"],
            "ID-CARD-ISSUANCE",
            ExternalOutcomeEvent.Generic,
            [HrManagementRoles.IdCard],
            "/ui/idcard/"),
        new(
            DisciplinaryActionEntityType,
            ServiceClientIds.Compliance,
            ["DISCIPLINARY-APPROVAL"],
            "DISCIPLINARY-APPROVAL",
            ExternalOutcomeEvent.Generic,
            [HrManagementRoles.Compliance],
            "/ui/compliance/"),
        new(
            SeparationRecordEntityType,
            ServiceClientIds.HrContracts,
            ["SEPARATION-APPROVAL", "SEPARATION-APPROVAL-SENSITIVE"],
            "SEPARATION-APPROVAL",
            ExternalOutcomeEvent.Generic,
            [HrManagementRoles.Compliance],
            "/ui/hr-contracts/")
    ];

    public static ExternalApprovalType? Find(string? entityType) =>
        All.FirstOrDefault(t => string.Equals(t.EntityType, entityType, StringComparison.OrdinalIgnoreCase));

    public static bool IsRegistered(string? entityType) => Find(entityType) is not null;

    /// <summary>The types whose owner hears the outcome through <c>IWorkflowOutcomeDecided</c>.</summary>
    public static bool IsGeneric(string? entityType) => Find(entityType)?.OutcomeEvent == ExternalOutcomeEvent.Generic;
}
