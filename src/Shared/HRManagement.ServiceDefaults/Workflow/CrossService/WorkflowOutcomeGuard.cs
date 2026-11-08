using HRManagement.MessageContracts.Events;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Workflow.CrossService;

/// <summary>
/// PLAN-46 (section 1, rule 6): what every owner checks before it applies an outcome. The owner's own closed state beats a late outcome,
/// and whatever is ignored is logged and counted in <c>workflow_outcome_ignored_total{entity_type,reason}</c>.
/// </summary>
public static class WorkflowOutcomeGuard
{
    public const int SupportedSchemaVersion = 1;

    public const string ReasonSchemaVersion = "schema_version";
    public const string ReasonUnknownOutcome = "unknown_outcome";
    public const string ReasonInstanceMismatch = "instance_mismatch";
    public const string ReasonNotPending = "not_pending";
    public const string ReasonExcludedDecider = "excluded_decider";

    private static readonly string[] Reasons =
        [ReasonSchemaVersion, ReasonUnknownOutcome, ReasonInstanceMismatch, ReasonNotPending, ReasonExcludedDecider];

    /// <summary>Applies only a known outcome of a supported version, to the record linked to the instance, while it is pending.</summary>
    public static bool ShouldApply(Guid? entityInstanceId, bool isPending, IWorkflowOutcomeDecided message, ILogger? logger = null)
    {
        if (!EventSchemaVersion.Supports(
                message.SchemaVersion, SupportedSchemaVersion, logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, nameof(IWorkflowOutcomeDecided)))
            return Ignore(message, ReasonSchemaVersion, logger);

        if (!WorkflowApprovalOutcomes.IsFinal(message.Outcome))
            return Ignore(message, ReasonUnknownOutcome, logger);

        if (entityInstanceId != message.InstanceId)
            return Ignore(message, ReasonInstanceMismatch, logger);

        return isPending || Ignore(message, ReasonNotPending, logger);
    }

    /// <summary>Defense in depth: the engine bars the excluded users, and an approval that still names one is refused and counted.</summary>
    public static bool HasExcludedDecider(IWorkflowOutcomeDecided message, IEnumerable<Guid?> excluded, ILogger? logger = null)
    {
        var barred = excluded.Where(id => id is not null && id != Guid.Empty).ToHashSet();
        if (!message.Decisions.Any(d => d.UserId is not null && barred.Contains(d.UserId)
                && d.Action == WorkflowApprovalOutcomes.ActionApproved))
            return false;

        Ignore(message, ReasonExcludedDecider, logger);
        return true;
    }

    /// <summary>A zero for every reason, so the series exists before the first ignored outcome.</summary>
    public static void TouchMetric(string entityType)
    {
        foreach (var reason in Reasons)
            WorkflowMetrics.OutcomeIgnored.Add(0, Tag("entity_type", entityType), Tag("reason", reason));
    }

    private static bool Ignore(IWorkflowOutcomeDecided message, string reason, ILogger? logger)
    {
        WorkflowMetrics.OutcomeIgnored.Add(1, Tag("entity_type", message.EntityType), Tag("reason", reason));
        logger?.LogWarning(
            "Ignored outcome of instance {InstanceId} for record {EntityId}: {Reason}.", message.InstanceId, message.EntityId, reason);
        return false;
    }

    private static KeyValuePair<string, object?> Tag(string name, object? value) => new(name, value);
}
