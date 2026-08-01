using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Domain.Services;

/// <summary>
/// Multi-step approval helpers for HR.Core with SoD self-approve and financial escalation (IAM-003).
/// WF-001: Bridge method CreateViaEngineOrLegacy supports dual-path creation of both ApprovalRequest
/// and optionally a WorkflowInstance when a published workflow definition exists.
/// </summary>
public static class ApprovalWorkflowService
{
    /// <summary>
    /// WF-001 bridge: Creates both a legacy ApprovalRequest and optionally a WorkflowInstance
    /// when a published WorkflowDefinition exists for the entity type.
    /// Returns the ApprovalRequest plus optionally a WorkflowInstance for persistence by the caller.
    /// </summary>
    public static (ApprovalRequest Request, WorkflowInstance? Instance) CreateViaEngineOrLegacy(
        string entityType,
        Guid entityId,
        string? description,
        Guid? requestedBy,
        string? requestedByName,
        string? payloadJson = null,
        string[]? requiredRoles = null,
        decimal? financialAmount = null,
        WorkflowDefinitionSnapshot? workflowDefinition = null)
    {
        var request = CreateApprovalRequest(
            entityType, entityId, description, requestedBy, requestedByName,
            payloadJson, requiredRoles, financialAmount);

        WorkflowInstance? instance = null;

        if (workflowDefinition is not null && workflowDefinition.Steps.Count > 0)
        {
            instance = new WorkflowInstance
            {
                DefinitionId = workflowDefinition.Id,
                DefinitionVersion = workflowDefinition.Version,
                EntityType = entityType,
                EntityId = entityId,
                CurrentStepLevel = 1,
                Status = WorkflowStatus.InProgress,
                ContextJson = payloadJson,
                InitiatedBy = requestedBy,
                InitiatedByName = requestedByName,
                StartedAt = DateTime.UtcNow,
                ApprovalRequestId = request.Id
            };
        }

        return (request, instance);
    }

    public static ApprovalRequest CreateApprovalRequest(
        string entityType,
        Guid entityId,
        string? description,
        Guid? requestedBy,
        string? requestedByName,
        string? payloadJson = null,
        string[]? requiredRoles = null,
        decimal? financialAmount = null)
    {
        var sod = new SeparationOfDutiesService();
        var requiredCount = sod.GetRequiredApprovalCount(entityType, financialAmount);

        var roles = requiredRoles ?? ["hr-core"];
        if (requiredCount >= 2 && roles.Length < 2)
        {
            roles = [roles[0], "hr-management-approver"];
        }

        var request = new ApprovalRequest
        {
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            RequestedBy = requestedBy,
            RequestedByName = requestedByName,
            PayloadJson = payloadJson,
            CurrentLevel = 1,
            Status = ApprovalStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        for (var i = 0; i < roles.Length; i++)
        {
            request.Steps.Add(new ApprovalStep
            {
                Level = i + 1,
                RoleRequired = roles[i],
                Status = ApprovalStatus.Pending
            });
        }

        return request;
    }

    public static bool TryApproveStep(
        ApprovalRequest request,
        Guid? reviewerId,
        string? reviewerName,
        string? comments,
        out string? error)
    {
        error = null;
        if (request.Status != ApprovalStatus.Pending)
        {
            error = "هذا الطلب ليس في حالة انتظار";
            return false;
        }

        if (reviewerId.HasValue && request.RequestedBy.HasValue && reviewerId == request.RequestedBy)
        {
            error = "لا يمكن للمنشئ اعتماد عمله الخاص.";
            return false;
        }

        var priorApprovers = request.Steps
            .Where(s => s.Status == ApprovalStatus.Approved && s.ReviewedBy.HasValue)
            .Select(s => s.ReviewedBy!.Value)
            .ToHashSet();

        if (reviewerId.HasValue && priorApprovers.Contains(reviewerId.Value))
        {
            error = "يتطلب الاعتماد المزدوج موافقة شخص ثانٍ مختلف.";
            return false;
        }

        var sod = new SeparationOfDutiesService();
        var lastApprover = priorApprovers.LastOrDefault();
        Guid? lastApproverId = priorApprovers.Count > 0 ? lastApprover : null;
        var sodResult = sod.ValidateAction(
            reviewerId ?? Guid.Empty,
            null,
            request.EntityType,
            request.EntityId,
            SodAction.Check,
            request.RequestedBy,
            lastApproverId);
        if (!sodResult.Allowed)
        {
            error = sodResult.DenialReason;
            return false;
        }

        var currentStep = request.Steps.FirstOrDefault(s => s.Level == request.CurrentLevel && s.Status == ApprovalStatus.Pending);
        if (currentStep is not null)
        {
            currentStep.Status = ApprovalStatus.Approved;
            currentStep.ReviewedBy = reviewerId;
            currentStep.ReviewedByName = reviewerName;
            currentStep.ReviewedAt = DateTime.UtcNow;
            currentStep.Comments = comments;
        }

        var nextStep = request.Steps
            .Where(s => s.Level > request.CurrentLevel && s.Status == ApprovalStatus.Pending)
            .OrderBy(s => s.Level)
            .FirstOrDefault();

        if (nextStep is not null)
        {
            request.CurrentLevel = nextStep.Level;
        }
        else
        {
            request.Status = ApprovalStatus.Approved;
            request.ReviewedBy = reviewerId;
            request.ReviewedByName = reviewerName;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewComments = comments;
        }

        return true;
    }

    public static bool TryReject(ApprovalRequest request, Guid? reviewerId, string? reviewerName, string? comments, out string? error)
    {
        error = null;
        if (request.Status != ApprovalStatus.Pending)
        {
            error = "هذا الطلب ليس في حالة انتظار";
            return false;
        }

        var currentStep = request.Steps.FirstOrDefault(s => s.Level == request.CurrentLevel && s.Status == ApprovalStatus.Pending);
        if (currentStep is not null)
        {
            currentStep.Status = ApprovalStatus.Rejected;
            currentStep.ReviewedBy = reviewerId;
            currentStep.ReviewedByName = reviewerName;
            currentStep.ReviewedAt = DateTime.UtcNow;
            currentStep.Comments = comments;
        }

        request.Status = ApprovalStatus.Rejected;
        request.ReviewedBy = reviewerId;
        request.ReviewedByName = reviewerName;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewComments = comments;
        return true;
    }
}
