using HR.Core.Domain.Services.RecruitmentKpi;
using HRManagement.SharedKernel.Workflow;

namespace HRManagement.UnitTests;

/// <summary>HCM-032 (decision 5): approval SLA resolution, current step age and completed step durations.</summary>
public class Hcm032ApprovalSlaRulesTests
{
    private static readonly DateTime Started = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private static WorkflowStepHistoryEntry Entry(int level, WorkflowStepStatus status, double hoursAfterStart, string name = "خطوة") =>
        new() { Level = level, StepName = name, Status = status, ActionAt = Started.AddHours(hoursAfterStart) };

    [Fact]
    public void Step_timeout_wins_over_the_options()
    {
        var sla = ApprovalSlaRules.ResolveSla(12, "RECRUITMENT-OFFER-APPROVAL",
            new Dictionary<string, int> { ["RECRUITMENT-OFFER-APPROVAL"] = 24 }, 48);
        Assert.Equal(new ApprovalSla(12, ApprovalSlaSource.StepTimeout), sla);
    }

    [Fact]
    public void Per_workflow_option_applies_case_insensitively_then_the_default()
    {
        var perCode = new Dictionary<string, int> { ["recruitment-offer-approval"] = 24, ["RECRUITMENT-HIRE"] = 0 };

        Assert.Equal(new ApprovalSla(24, ApprovalSlaSource.WorkflowOption),
            ApprovalSlaRules.ResolveSla(null, "RECRUITMENT-OFFER-APPROVAL", perCode, 48));
        // Zero or missing per-code values fall through to the default.
        Assert.Equal(new ApprovalSla(48, ApprovalSlaSource.Default),
            ApprovalSlaRules.ResolveSla(null, "RECRUITMENT-HIRE", perCode, 48));
        Assert.Equal(new ApprovalSla(72, ApprovalSlaSource.Default),
            ApprovalSlaRules.ResolveSla(0, "RECRUITMENT-APPEAL-REVIEW", null, 72));
        Assert.Equal(new ApprovalSla(48, ApprovalSlaSource.Default),
            ApprovalSlaRules.ResolveSla(null, "X", null, 0));
    }

    [Fact]
    public void Current_step_starts_at_the_last_action_or_the_instance_start()
    {
        Assert.Equal(Started, ApprovalSlaRules.CurrentStepStart([], Started));
        Assert.Equal(Started.AddHours(30),
            ApprovalSlaRules.CurrentStepStart([Entry(1, WorkflowStepStatus.Approved, 5), Entry(2, WorkflowStepStatus.Approved, 30)], Started));
    }

    [Fact]
    public void Overdue_only_when_the_age_exceeds_the_sla()
    {
        var sla = new ApprovalSla(48, ApprovalSlaSource.Default);
        var start = Started;

        Assert.Equal(48m, ApprovalSlaRules.AgeHours(start, start.AddHours(48)));
        Assert.False(ApprovalSlaRules.IsOverdue(ApprovalSlaRules.AgeHours(start, start.AddHours(48)), sla));
        Assert.True(ApprovalSlaRules.IsOverdue(ApprovalSlaRules.AgeHours(start, start.AddHours(48.5)), sla));
    }

    [Fact]
    public void Completed_steps_chain_from_the_start_and_skipped_steps_only_move_the_start()
    {
        var history = new[]
        {
            Entry(1, WorkflowStepStatus.Approved, 10, "مدير الموارد البشرية"),
            Entry(2, WorkflowStepStatus.Skipped, 10, "المالية"),
            Entry(3, WorkflowStepStatus.Rejected, 40, "المدير العام")
        };

        var steps = ApprovalSlaRules.CompletedSteps(history, Started);

        Assert.Equal(2, steps.Count);
        Assert.Equal((1, "مدير الموارد البشرية", 10m), (steps[0].Level, steps[0].StepName, steps[0].Hours));
        Assert.Equal((3, WorkflowStepStatus.Rejected, 30m), (steps[1].Level, steps[1].Status, steps[1].Hours));
    }
}
