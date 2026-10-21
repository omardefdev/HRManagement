using System.Text.Json;
using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Domain.Services;

/// <summary>HCM-027: merit override offers need a distinct approver for every workflow step.</summary>
public static class OfferOverrideApprovalRules
{
    public const string WorkflowCode = "RECRUITMENT-OFFER-OVERRIDE-APPROVAL";

    public const string SameApproverMessage = "لا يجوز لنفس المعتمد اعتماد أكثر من خطوة في عرض استثنائي.";

    public static bool IsOverrideWorkflow(string? definitionCode) =>
        string.Equals(definitionCode, WorkflowCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="actorUserId"/> approved a step before <paramref name="currentStepLevel"/>.</summary>
    public static bool HasApprovedEarlierStep(string? stepHistoryJson, int currentStepLevel, Guid? actorUserId)
    {
        if (actorUserId is null || string.IsNullOrWhiteSpace(stepHistoryJson))
            return false;

        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(stepHistoryJson) ?? [];
        return history.Any(h => h.Status == WorkflowStepStatus.Approved
            && h.ActorId == actorUserId
            && h.Level < currentStepLevel);
    }
}
