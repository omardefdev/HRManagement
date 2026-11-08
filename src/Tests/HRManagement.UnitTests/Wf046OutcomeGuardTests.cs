using System.Diagnostics.Metrics;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.TestSupport.Workflow;

namespace HRManagement.UnitTests;

/// <summary>PLAN-46: a wrong instance, a closed record, a newer version, an unknown outcome, an excluded decider. Each is refused and counted.</summary>
public sealed class Wf046OutcomeGuardTests
{
    private static readonly Guid Instance = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();

    private const string EntityType = "Wf046GuardTest";

    private static IWorkflowOutcomeDecided Message(
        string outcome = WorkflowApprovalOutcomes.Approved, int schemaVersion = 1, IReadOnlyList<WorkflowStepDecisionDto>? decisions = null) =>
        FakeWorkflowApprovalClient.Outcome(EntityType, Guid.NewGuid(), Instance, outcome, decisions: decisions, schemaVersion: schemaVersion);

    [Theory]
    [InlineData(WorkflowApprovalOutcomes.Approved)]
    [InlineData(WorkflowApprovalOutcomes.Rejected)]
    [InlineData(WorkflowApprovalOutcomes.Cancelled)]
    public void A_linked_pending_record_takes_the_outcome(string outcome) =>
        Assert.True(WorkflowOutcomeGuard.ShouldApply(Instance, isPending: true, Message(outcome)));

    [Fact]
    public void Another_instance_is_ignored_and_counted() =>
        AssertIgnored(WorkflowOutcomeGuard.ReasonInstanceMismatch, () => WorkflowOutcomeGuard.ShouldApply(Guid.NewGuid(), isPending: true, Message()));

    [Fact]
    public void A_record_linked_to_nothing_is_ignored() =>
        AssertIgnored(WorkflowOutcomeGuard.ReasonInstanceMismatch, () => WorkflowOutcomeGuard.ShouldApply(null, isPending: true, Message()));

    [Fact]
    public void A_record_the_owner_closed_keeps_its_state() =>
        AssertIgnored(WorkflowOutcomeGuard.ReasonNotPending, () => WorkflowOutcomeGuard.ShouldApply(Instance, isPending: false, Message()));

    [Fact]
    public void A_newer_version_is_ignored() =>
        AssertIgnored(WorkflowOutcomeGuard.ReasonSchemaVersion, () => WorkflowOutcomeGuard.ShouldApply(Instance, isPending: true, Message(schemaVersion: 2)));

    [Theory]
    [InlineData("InProgress")]
    [InlineData("Completed")]
    [InlineData("")]
    public void A_word_outside_the_contract_is_ignored(string outcome) =>
        AssertIgnored(WorkflowOutcomeGuard.ReasonUnknownOutcome, () => WorkflowOutcomeGuard.ShouldApply(Instance, isPending: true, Message(outcome)));

    [Fact]
    public void The_creator_among_the_approvers_is_found_and_counted()
    {
        var decisions = new[]
        {
            new WorkflowStepDecisionDto(1, "compliance", Guid.NewGuid(), "Approved", DateTime.UtcNow),
            new WorkflowStepDecisionDto(2, "hr-management-approver", Creator, "Approved", DateTime.UtcNow)
        };

        var ignored = Measure(WorkflowOutcomeGuard.ReasonExcludedDecider, () =>
            Assert.True(WorkflowOutcomeGuard.HasExcludedDecider(Message(decisions: decisions), [Creator])));

        Assert.Equal(1, ignored);
    }

    [Fact]
    public void Other_approvers_pass()
    {
        var decisions = new[] { new WorkflowStepDecisionDto(1, "compliance", Guid.NewGuid(), "Approved", DateTime.UtcNow) };

        Assert.False(WorkflowOutcomeGuard.HasExcludedDecider(Message(decisions: decisions), [Creator, null]));
    }

    [Fact]
    public void A_withdrawal_by_the_creator_is_no_decision_against_him()
    {
        var decisions = new[] { new WorkflowStepDecisionDto(1, "compliance", Creator, "Withdrawn", DateTime.UtcNow) };

        Assert.False(WorkflowOutcomeGuard.HasExcludedDecider(Message(WorkflowApprovalOutcomes.Cancelled, decisions: decisions), [Creator]));
    }

    private static void AssertIgnored(string reason, Func<bool> apply)
    {
        var applied = true;
        var ignored = Measure(reason, () => applied = apply());

        Assert.False(applied);
        Assert.Equal(1, ignored);
    }

    /// <summary>The increments of <c>workflow_outcome_ignored_total</c> for this test's type and <paramref name="reason"/>.</summary>
    private static long Measure(string reason, Action action)
    {
        // The counter is created on first use: a listener started before that would miss the instrument.
        _ = WorkflowMetrics.OutcomeIgnored;

        long total = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "workflow_outcome_ignored_total")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var type = false;
            var matches = false;
            foreach (var tag in tags)
            {
                type |= tag.Key == "entity_type" && (string?)tag.Value == EntityType;
                matches |= tag.Key == "reason" && (string?)tag.Value == reason;
            }

            if (type && matches)
                Interlocked.Add(ref total, value);
        });
        listener.Start();

        action();
        return Interlocked.Read(ref total);
    }
}
