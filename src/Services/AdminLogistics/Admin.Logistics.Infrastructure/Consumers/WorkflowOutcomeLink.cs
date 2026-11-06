using HRManagement.ServiceDefaults.Workflow;
using Microsoft.Extensions.Logging;

namespace Admin.Logistics.Infrastructure.Consumers;

/// <summary>
/// PLAN-43 (G-WF-05): a custody outcome counts only for the workflow instance the record is linked to, so an instance someone else
/// started for the same id decides nothing. A v1 message names no instance and is ignored.
/// </summary>
internal static class WorkflowOutcomeLink
{
    public const int SupportedSchemaVersion = 2;

    public static bool IsCurrentVersion(int schemaVersion, ILogger logger, string eventName)
    {
        if (schemaVersion >= SupportedSchemaVersion)
            return true;

        logger.LogWarning("Ignoring {EventName}: SchemaVersion {SchemaVersion} names no workflow instance", eventName, schemaVersion);
        WorkflowMetrics.OutcomeIgnored.Add(1, new KeyValuePair<string, object?>("reason", "schema_version"));
        return false;
    }

    public static bool IsLinked(Guid? outcomeInstanceId, Guid? recordInstanceId, Guid recordId, ILogger logger, string eventName)
    {
        if (outcomeInstanceId is not null && outcomeInstanceId == recordInstanceId)
            return true;

        logger.LogWarning(
            "Ignoring {EventName} for {RecordId}: instance {OutcomeInstanceId} is not the linked instance {RecordInstanceId}",
            eventName, recordId, outcomeInstanceId, recordInstanceId);
        WorkflowMetrics.OutcomeIgnored.Add(1, new KeyValuePair<string, object?>("reason", "unlinked"));
        return false;
    }
}
