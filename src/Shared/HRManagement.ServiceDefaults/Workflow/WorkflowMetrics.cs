using System.Diagnostics.Metrics;

namespace HRManagement.ServiceDefaults.Workflow;

/// <summary>PLAN-43: the counters of the workflow engine and of the services that consume its outcomes.</summary>
public static class WorkflowMetrics
{
    public const string MeterName = "HRManagement.Workflow";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> DefinitionInvalid = Meter.CreateCounter<long>(
        "workflow_definition_invalid_total",
        description: "Published workflow definitions that failed the validator at start-up");

    public static readonly Counter<long> OutcomeIgnored = Meter.CreateCounter<long>(
        "workflow_outcome_ignored_total",
        description: "Workflow outcomes a consumer refused because the instance is not linked to the record");

    public static readonly Counter<long> ApprovalSweepApplied = Meter.CreateCounter<long>(
        "workflow_approval_sweep_applied_total",
        description: "Finished outcomes the owner-side sweep applied because the event did not reach the record");
}
