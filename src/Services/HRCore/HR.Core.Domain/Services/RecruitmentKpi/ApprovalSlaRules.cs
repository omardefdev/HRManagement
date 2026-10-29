using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Domain.Services.RecruitmentKpi;

public enum ApprovalSlaSource
{
    /// <summary>The step's <c>TimeoutHours</c> in the pinned workflow definition.</summary>
    StepTimeout = 0,

    /// <summary><c>Recruitment:Analytics:ApprovalSlaHours:{WorkflowCode}</c>.</summary>
    WorkflowOption = 1,

    /// <summary><c>Recruitment:Analytics:DefaultApprovalSlaHours</c>.</summary>
    Default = 2
}

public sealed record ApprovalSla(int Hours, ApprovalSlaSource Source);

/// <summary>A step that ended (approved or rejected) and how long it took.</summary>
public sealed record CompletedApprovalStep(int Level, string StepName, WorkflowStepStatus Status, DateTime StartedAt, DateTime EndedAt)
{
    public decimal Hours => (decimal)(EndedAt - StartedAt).TotalHours;
}

/// <summary>
/// HCM-032 (PLAN-32, decision 5): <c>recruitment_approval_sla</c>. SLA = the step's <c>TimeoutHours</c> when set, else the
/// per-workflow option, else the default (48 h) — an organizational policy value, not a legal one. A step starts at the
/// <c>ActionAt</c> of the latest history entry (the previous step's action), or at the instance's <c>StartedAt</c> when
/// there is none.
/// </summary>
public static class ApprovalSlaRules
{
    public const int FallbackDefaultHours = 48;

    public static ApprovalSla ResolveSla(
        int? stepTimeoutHours,
        string workflowCode,
        IReadOnlyDictionary<string, int>? perWorkflowHours,
        int defaultHours)
    {
        if (stepTimeoutHours is > 0)
            return new ApprovalSla(stepTimeoutHours.Value, ApprovalSlaSource.StepTimeout);
        if (perWorkflowHours is not null)
        {
            foreach (var (code, hours) in perWorkflowHours)
            {
                if (hours > 0 && string.Equals(code, workflowCode, StringComparison.OrdinalIgnoreCase))
                    return new ApprovalSla(hours, ApprovalSlaSource.WorkflowOption);
            }
        }
        return new ApprovalSla(defaultHours > 0 ? defaultHours : FallbackDefaultHours, ApprovalSlaSource.Default);
    }

    public static DateTime CurrentStepStart(IReadOnlyList<WorkflowStepHistoryEntry> history, DateTime startedAt) =>
        history.Count == 0 ? startedAt : history.Max(h => h.ActionAt);

    public static decimal AgeHours(DateTime stepStart, DateTime nowUtc) => (decimal)(nowUtc - stepStart).TotalHours;

    public static bool IsOverdue(decimal ageHours, ApprovalSla sla) => ageHours > sla.Hours;

    /// <summary>Approved and rejected steps with their duration; skipped steps only move the next step's start.</summary>
    public static IReadOnlyList<CompletedApprovalStep> CompletedSteps(
        IReadOnlyList<WorkflowStepHistoryEntry> history,
        DateTime startedAt)
    {
        var result = new List<CompletedApprovalStep>();
        var start = startedAt;
        foreach (var entry in history.OrderBy(h => h.ActionAt))
        {
            if (entry.Status is WorkflowStepStatus.Approved or WorkflowStepStatus.Rejected)
                result.Add(new CompletedApprovalStep(entry.Level, entry.StepName, entry.Status, start, entry.ActionAt));
            start = entry.ActionAt;
        }
        return result;
    }
}
