using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HRManagement.ServiceDefaults.Workflow;

namespace HR.Core.Web.Services;

public static class WorkInboxItemMapper
{
    public const string SourceSystemHrCore = "hr-core";
    public const string SourceLegacy = "legacy-approval";

    public static WorkInboxItem FromWorkflow(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowDeepLinkInfo link,
        Guid assigneeId) =>
        new()
        {
            Id = instance.Id,
            TenantId = instance.TenantId,
            AssigneeId = assigneeId,
            SourceSystem = SourceSystemHrCore,
            SourceReferenceId = instance.Id.ToString(),
            ItemType = WorkItemType.Approval,
            Priority = WorkItemPriority.Normal,
            Status = WorkItemStatus.Pending,
            Title = link.Title,
            Description = BuildWorkflowDescription(link, instance, definition),
            DeepLink = link.DeepLink,
            CreatedAt = instance.StartedAt
        };

    public static WorkInboxItem FromLegacyApproval(
        ApprovalRequest request,
        ApprovalStep currentStep,
        Guid assigneeId) =>
        new()
        {
            Id = request.Id,
            TenantId = request.TenantId,
            AssigneeId = assigneeId,
            SourceSystem = SourceLegacy,
            SourceReferenceId = request.Id.ToString(),
            ItemType = WorkItemType.Approval,
            Priority = WorkItemPriority.Normal,
            Status = WorkItemStatus.Pending,
            Title = string.IsNullOrWhiteSpace(request.Description)
                ? $"اعتماد legacy — {request.EntityType}"
                : request.Description,
            Description = $"legacy · {request.EntityType} · المستوى {currentStep.Level} ({currentStep.RoleRequired})",
            DeepLink = $"/Approvals/Review/{request.Id}",
            CreatedAt = request.RequestedAt
        };

    private static string BuildWorkflowDescription(
        WorkflowDeepLinkInfo link,
        WorkflowInstance instance,
        WorkflowDefinition definition) =>
        $"{link.EntityTypeLabel} · {definition.Code} · الخطوة {instance.CurrentStepLevel}"
        + (link.CurrentStepName is null ? "" : $" ({link.CurrentStepName})");
}
