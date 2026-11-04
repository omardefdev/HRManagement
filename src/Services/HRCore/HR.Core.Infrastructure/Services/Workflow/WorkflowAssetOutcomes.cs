using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>
/// The custody outcomes of a completed or rejected <c>AssetAssignment</c>/<c>AssetLossReport</c> workflow (consumed by
/// Admin.Logistics). PLAN-40 (D-40-3): the message id is derived from the workflow instance, so the original publish and a
/// replay (<c>MessagingRepairController</c>) are the same message for the consumer's inbox. Both methods only stage the
/// message in the bus outbox: the caller saves.
/// </summary>
public static class WorkflowAssetOutcomes
{
    public const string AssignmentEntityType = "AssetAssignment";
    public const string LossEntityType = "AssetLossReport";

    /// <summary>The ledger types of the four outcomes (<see cref="PublishedEventLedger"/>).</summary>
    public static readonly IReadOnlyList<string> LedgerEntityTypes =
    [
        PublishedEventLedger.EntityType<IAssetAssignmentApproved>(),
        PublishedEventLedger.EntityType<IAssetAssignmentRejected>(),
        PublishedEventLedger.EntityType<IAssetLossApproved>(),
        PublishedEventLedger.EntityType<IAssetLossRejected>()
    ];

    public static bool IsAssetWorkflow(WorkflowInstance instance) =>
        string.Equals(instance.EntityType, AssignmentEntityType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(instance.EntityType, LossEntityType, StringComparison.OrdinalIgnoreCase);

    /// <summary>Stages the outcome of an asset workflow and its ledger row; the caller's save writes both with the instance.</summary>
    [StagesOutboxMessages]
    public static async Task PublishOutcomeAsync(
        HRCoreDbContext db, IPublishEndpoint publish, WorkflowInstance instance, bool approved, string? reason, Guid? actor,
        string ledgerAction = PublishedEventLedger.Published, CancellationToken ct = default)
    {
        var assignment = string.Equals(instance.EntityType, AssignmentEntityType, StringComparison.OrdinalIgnoreCase);
        if (assignment)
            await PublishAssignmentOutcomeAsync(publish, instance, approved, reason, ct);
        else
            await PublishLossOutcomeAsync(publish, instance, approved, reason, ct);

        db.AuditLogs.Add((assignment, approved) switch
        {
            (true, true) => PublishedEventLedger.Entry<IAssetAssignmentApproved>(instance.Id, actor, ledgerAction),
            (true, false) => PublishedEventLedger.Entry<IAssetAssignmentRejected>(instance.Id, actor, ledgerAction),
            (false, true) => PublishedEventLedger.Entry<IAssetLossApproved>(instance.Id, actor, ledgerAction),
            (false, false) => PublishedEventLedger.Entry<IAssetLossRejected>(instance.Id, actor, ledgerAction)
        });
    }

    [StagesOutboxMessages]
    private static async Task PublishAssignmentOutcomeAsync(
        IPublishEndpoint publish, WorkflowInstance instance, bool approved, string? reason = null, CancellationToken ct = default)
    {
        var ctx = ParseAssignmentContext(instance.ContextJson);
        if (approved)
        {
            await publish.Publish<IAssetAssignmentApproved>(new
            {
                AssignmentId = instance.EntityId,
                AssetId = ctx.AssetId,
                EmployeeId = ctx.EmployeeId,
                OccurredAt = instance.CompletedAt ?? DateTime.UtcNow,
                SourceSystem = "hr-core",
                SchemaVersion = 1
            }, p => p.MessageId = DeterministicMessageId.For<IAssetAssignmentApproved>(instance.Id), ct);
        }
        else
        {
            await publish.Publish<IAssetAssignmentRejected>(new
            {
                AssignmentId = instance.EntityId,
                AssetId = ctx.AssetId,
                Reason = reason,
                OccurredAt = instance.CompletedAt ?? DateTime.UtcNow,
                SourceSystem = "hr-core",
                SchemaVersion = 1
            }, p => p.MessageId = DeterministicMessageId.For<IAssetAssignmentRejected>(instance.Id), ct);
        }
    }

    [StagesOutboxMessages]
    private static async Task PublishLossOutcomeAsync(
        IPublishEndpoint publish, WorkflowInstance instance, bool approved, string? reason = null, CancellationToken ct = default)
    {
        var ctx = ParseLossContext(instance.ContextJson);
        if (approved)
        {
            await publish.Publish<IAssetLossApproved>(new
            {
                LossReportId = instance.EntityId,
                AssetId = ctx.AssetId,
                OccurredAt = instance.CompletedAt ?? DateTime.UtcNow,
                SourceSystem = "hr-core",
                SchemaVersion = 1
            }, p => p.MessageId = DeterministicMessageId.For<IAssetLossApproved>(instance.Id), ct);
        }
        else
        {
            await publish.Publish<IAssetLossRejected>(new
            {
                LossReportId = instance.EntityId,
                AssetId = ctx.AssetId,
                Reason = reason,
                OccurredAt = instance.CompletedAt ?? DateTime.UtcNow,
                SourceSystem = "hr-core",
                SchemaVersion = 1
            }, p => p.MessageId = DeterministicMessageId.For<IAssetLossRejected>(instance.Id), ct);
        }
    }

    /// <summary>The message id the outcome of <paramref name="instance"/> carries (D-40-3).</summary>
    public static Guid MessageIdFor(WorkflowInstance instance, bool approved) =>
        (string.Equals(instance.EntityType, AssignmentEntityType, StringComparison.OrdinalIgnoreCase), approved) switch
        {
            (true, true) => DeterministicMessageId.For<IAssetAssignmentApproved>(instance.Id),
            (true, false) => DeterministicMessageId.For<IAssetAssignmentRejected>(instance.Id),
            (false, true) => DeterministicMessageId.For<IAssetLossApproved>(instance.Id),
            (false, false) => DeterministicMessageId.For<IAssetLossRejected>(instance.Id),
        };

    private static (Guid AssetId, Guid LossReportId) ParseLossContext(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (Guid.Empty, Guid.Empty);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var assetId = root.TryGetProperty("assetId", out var a) && Guid.TryParse(a.GetString(), out var ag) ? ag : Guid.Empty;
            var lossReportId = root.TryGetProperty("lossReportId", out var l) && Guid.TryParse(l.GetString(), out var lg) ? lg : Guid.Empty;
            return (assetId, lossReportId);
        }
        catch
        {
            return (Guid.Empty, Guid.Empty);
        }
    }

    private static (Guid AssetId, Guid EmployeeId) ParseAssignmentContext(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (Guid.Empty, Guid.Empty);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var assetId = root.TryGetProperty("assetId", out var a) && Guid.TryParse(a.GetString(), out var ag) ? ag : Guid.Empty;
            var employeeId = root.TryGetProperty("employeeId", out var e) && Guid.TryParse(e.GetString(), out var eg) ? eg : Guid.Empty;
            return (assetId, employeeId);
        }
        catch
        {
            return (Guid.Empty, Guid.Empty);
        }
    }
}
