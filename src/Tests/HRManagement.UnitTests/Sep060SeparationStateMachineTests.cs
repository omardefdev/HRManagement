using System.Text.RegularExpressions;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-60 (§1, P-4): the separation record's state machine — every allowed pair and every refused one, the message of each refusal, the
/// fields a transition writes, the derived flag, the backfill's rules for an older record, and the transaction that follows the record.
/// </summary>
public sealed class Sep060SeparationStateMachineTests
{
    private static readonly Dictionary<SeparationStatus, SeparationStatus[]> Expected = new()
    {
        [SeparationStatus.Draft] = [SeparationStatus.PendingApproval, SeparationStatus.Cancelled],
        [SeparationStatus.PendingApproval] =
            [SeparationStatus.Approved, SeparationStatus.Rejected, SeparationStatus.Draft, SeparationStatus.Cancelled],
        [SeparationStatus.Rejected] = [SeparationStatus.Draft, SeparationStatus.Cancelled],
        [SeparationStatus.Approved] = [SeparationStatus.Executing, SeparationStatus.Cancelled],
        [SeparationStatus.Executing] = [SeparationStatus.AwaitingIntervention, SeparationStatus.Completed],
        [SeparationStatus.AwaitingIntervention] = [SeparationStatus.Executing],
        [SeparationStatus.Completed] = [],
        [SeparationStatus.Cancelled] = []
    };

    public static TheoryData<SeparationStatus, SeparationStatus> AllPairs()
    {
        var data = new TheoryData<SeparationStatus, SeparationStatus>();
        foreach (var from in Enum.GetValues<SeparationStatus>())
            foreach (var to in Enum.GetValues<SeparationStatus>())
                data.Add(from, to);
        return data;
    }

    private static SeparationRecord RecordIn(SeparationStatus status)
    {
        var record = new SeparationRecord { EmployeeId = Guid.NewGuid(), EffectiveDate = new DateOnly(2026, 12, 1) };
        SeparationTestJourney.MoveTo(record, status);
        return record;
    }

    private static SeparationSubmission Submission() => new(Guid.NewGuid(), "SEPARATION-APPROVAL", 1);

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Each_pair_is_allowed_exactly_when_the_table_says_so(SeparationStatus from, SeparationStatus to)
    {
        var allowed = Expected[from].Contains(to);
        Assert.Equal(allowed, SeparationWorkflowRules.CanTransition(from, to));

        var record = RecordIn(from);
        if (allowed)
        {
            SeparationWorkflowRules.Transition(record, to, Guid.NewGuid(), "سبب الاختبار",
                to == SeparationStatus.PendingApproval ? Submission() : null);
            Assert.Equal(to, record.Status);
            return;
        }

        var refused = Assert.Throws<SeparationTransitionException>(() =>
            SeparationWorkflowRules.Transition(record, to, Guid.NewGuid(), "سبب الاختبار", Submission()));
        Assert.Equal(SeparationWorkflowRules.RefusalFor(from, to), refused.Message);
        Assert.Equal(from, record.Status);
    }

    [Theory]
    [InlineData(SeparationStatus.Executing)]
    [InlineData(SeparationStatus.AwaitingIntervention)]
    [InlineData(SeparationStatus.Completed)]
    public void An_executed_separation_is_never_cancelled_here(SeparationStatus status)
    {
        var refused = Assert.Throws<SeparationTransitionException>(() =>
            SeparationWorkflowRules.Transition(RecordIn(status), SeparationStatus.Cancelled, Guid.NewGuid(), "سبب"));
        Assert.Equal("إلغاء فصل قيد التنفيذ يتطلب مسار التعويض (PLAN-63)", refused.Message);
        Assert.False(SeparationWorkflowRules.IsCancellable(status));
    }

    [Fact]
    public void A_refused_pair_names_both_statuses_in_arabic()
    {
        Assert.Equal("لا يمكن نقل سجل الفصل من «مسودة» إلى «معتمد».",
            SeparationWorkflowRules.RefusalFor(SeparationStatus.Draft, SeparationStatus.Approved));
        Assert.Equal(SeparationWorkflowRules.AlreadyCancelledMessage,
            SeparationWorkflowRules.RefusalFor(SeparationStatus.Cancelled, SeparationStatus.Cancelled));
    }

    [Fact]
    public void PendingApproval_is_refused_without_an_engine_instance()
    {
        var record = RecordIn(SeparationStatus.Draft);
        Assert.Equal(SeparationWorkflowRules.NoInstanceMessage,
            Assert.Throws<SeparationTransitionException>(() =>
                SeparationWorkflowRules.Transition(record, SeparationStatus.PendingApproval, Guid.NewGuid())).Message);
        Assert.Equal(SeparationWorkflowRules.NoInstanceMessage,
            Assert.Throws<SeparationTransitionException>(() =>
                SeparationWorkflowRules.Transition(record, SeparationStatus.PendingApproval, Guid.NewGuid(), null,
                    new SeparationSubmission(Guid.Empty, "SEPARATION-APPROVAL", 1))).Message);
        Assert.Equal(SeparationStatus.Draft, record.Status);
        Assert.Null(record.WorkflowInstanceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_cancellation_needs_a_reason(string? reason)
    {
        var record = RecordIn(SeparationStatus.Draft);
        Assert.Equal(SeparationWorkflowRules.CancelReasonRequiredMessage,
            Assert.Throws<SeparationTransitionException>(() =>
                SeparationWorkflowRules.Transition(record, SeparationStatus.Cancelled, Guid.NewGuid(), reason)).Message);
        Assert.False(record.IsCancelled);
    }

    [Fact]
    public void Each_transition_writes_what_its_status_means()
    {
        var submitter = Guid.NewGuid();
        var decider = Guid.NewGuid();
        var executor = Guid.NewGuid();
        var instance = Guid.NewGuid();
        var record = RecordIn(SeparationStatus.Draft);

        SeparationWorkflowRules.Transition(record, SeparationStatus.PendingApproval, submitter, null,
            new SeparationSubmission(instance, "SEPARATION-APPROVAL-SENSITIVE", 3));
        Assert.Equal((instance, "SEPARATION-APPROVAL-SENSITIVE", 3, submitter),
            (record.WorkflowInstanceId!.Value, record.WorkflowCode!, record.SubmissionNo, record.SubmittedBy!.Value));
        Assert.NotNull(record.SubmittedAt);

        SeparationWorkflowRules.Transition(record, SeparationStatus.Rejected, decider, "  ناقص المستندات  ");
        Assert.Equal((decider, "ناقص المستندات"), (record.DecidedBy!.Value, record.RejectionReason!));

        SeparationWorkflowRules.Transition(record, SeparationStatus.Draft, submitter, "إعادة فتح");
        SeparationWorkflowRules.Transition(record, SeparationStatus.PendingApproval, submitter, null,
            new SeparationSubmission(Guid.NewGuid(), "SEPARATION-APPROVAL", 4));
        Assert.Null(record.RejectionReason);
        Assert.Null(record.DecidedBy);

        SeparationWorkflowRules.Transition(record, SeparationStatus.Approved, decider);
        Assert.Equal(decider, record.DecidedBy);
        SeparationWorkflowRules.Transition(record, SeparationStatus.Executing, executor, "تنفيذ");
        Assert.Equal(executor, record.ExecutedBy);
        Assert.NotNull(record.ExecutedAt);

        // The intervention and the resume do not rewrite who executed.
        SeparationWorkflowRules.Transition(record, SeparationStatus.AwaitingIntervention, null, "تعثر طرف (النظام)");
        SeparationWorkflowRules.Transition(record, SeparationStatus.Executing, Guid.NewGuid(), "استئناف");
        Assert.Equal(executor, record.ExecutedBy);

        SeparationWorkflowRules.Transition(record, SeparationStatus.Completed, null, "اكتمال (النظام)");
        Assert.NotNull(record.CompletedAt);
    }

    [Fact]
    public void A_cancellation_keeps_the_record_with_its_flag_and_its_reason()
    {
        var record = RecordIn(SeparationStatus.Approved);
        SeparationWorkflowRules.Transition(record, SeparationStatus.Cancelled, Guid.NewGuid(), "  تراجع الموظف  ");
        Assert.True(record.IsCancelled);
        Assert.Equal("تراجع الموظف", record.CancellationReason);
        Assert.NotNull(record.CancelledAt);
        Assert.False(SeparationWorkflowRules.IsOpen(record.Status));
    }

    [Theory]
    [InlineData(SeparationStatus.Draft, false)]
    [InlineData(SeparationStatus.PendingApproval, false)]
    [InlineData(SeparationStatus.Approved, false)]
    [InlineData(SeparationStatus.Rejected, false)]
    [InlineData(SeparationStatus.Cancelled, false)]
    [InlineData(SeparationStatus.Executing, true)]
    [InlineData(SeparationStatus.AwaitingIntervention, true)]
    [InlineData(SeparationStatus.Completed, true)]
    public void The_published_flag_is_derived_from_the_status(SeparationStatus status, bool published) =>
        Assert.Equal(published, RecordIn(status).SeparationEventPublished);

    [Fact]
    public void The_open_statuses_are_the_five_the_partial_index_holds()
    {
        Assert.Equal(
            [SeparationStatus.Draft, SeparationStatus.PendingApproval, SeparationStatus.Approved, SeparationStatus.Executing, SeparationStatus.AwaitingIntervention],
            Enum.GetValues<SeparationStatus>().Where(SeparationWorkflowRules.IsOpen));
        foreach (var status in SeparationWorkflowRules.OpenStatuses)
            Assert.Contains($"'{status}'", HR.Contracts.Infrastructure.Data.ContractsDbContext.SeparationRecordOpenIndexFilter);
    }

    // ── The backfill's rules for a record written before the state machine (P-5), as the seed and the reconciler apply them.

    [Theory]
    [InlineData(true, false, null, SeparationStatus.Cancelled)]
    [InlineData(true, true, "Completed", SeparationStatus.Cancelled)]
    [InlineData(false, true, "Completed", SeparationStatus.Completed)]
    [InlineData(false, true, "AwaitingIntervention", SeparationStatus.AwaitingIntervention)]
    [InlineData(false, true, "Cancelled", SeparationStatus.Cancelled)]
    [InlineData(false, true, "AwaitingParties", SeparationStatus.Executing)]
    [InlineData(false, true, "Compensating", SeparationStatus.Executing)]
    [InlineData(false, true, "Faulted", SeparationStatus.Executing)]
    [InlineData(false, true, null, SeparationStatus.Executing)]
    [InlineData(false, false, null, SeparationStatus.Draft)]
    public void The_legacy_rules_give_each_combination_its_status(bool cancelled, bool published, string? saga, SeparationStatus expected) =>
        Assert.Equal(expected, SeparationWorkflowRules.LegacyStatus(cancelled, published, Guid.NewGuid(), saga).Status);

    [Fact]
    public void A_legacy_draft_with_no_employee_is_cancelled_with_plan60s_reason()
    {
        var (status, reason) = SeparationWorkflowRules.LegacyStatus(false, false, Guid.Empty, null);
        Assert.Equal(SeparationStatus.Cancelled, status);
        Assert.Equal(SeparationWorkflowRules.NoEmployeeReason, reason);
    }

    [Fact]
    public void The_seed_writes_a_published_record_executing_and_an_unpublished_one_a_marked_draft()
    {
        var published = new SeparationRecord { EmployeeId = Guid.NewGuid() };
        SeparationWorkflowRules.ApplyLegacyBackfill(published, wasPublished: true, sagaState: null);
        Assert.Equal((SeparationStatus.Executing, true, false),
            (published.Status, published.SeparationEventPublished, published.IsLegacyDraft));

        var draft = new SeparationRecord { EmployeeId = Guid.NewGuid() };
        SeparationWorkflowRules.ApplyLegacyBackfill(draft, wasPublished: false, sagaState: null);
        Assert.Equal((SeparationStatus.Draft, false, true), (draft.Status, draft.SeparationEventPublished, draft.IsLegacyDraft));
    }

    [Fact]
    public void The_reconciler_touches_only_a_record_whose_flags_and_status_disagree()
    {
        // In line: left as it is.
        foreach (var status in Enum.GetValues<SeparationStatus>())
            Assert.False(SeparationWorkflowRules.ReconcileFromLegacyFlags(RecordIn(status), "AwaitingParties"), status.ToString());

        // The previous tag published a draft (flag only): back in line by its saga's state.
        var back = RecordIn(SeparationStatus.Draft);
        typeof(SeparationRecord).GetProperty(nameof(SeparationRecord.SeparationEventPublished))!.SetValue(back, true);
        Assert.True(SeparationWorkflowRules.ReconcileFromLegacyFlags(back, "AwaitingIntervention"));
        Assert.Equal(SeparationStatus.AwaitingIntervention, back.Status);
        Assert.False(SeparationWorkflowRules.ReconcileFromLegacyFlags(back, "AwaitingIntervention"));

        // The previous tag cancelled an approved record (flag only): Cancelled.
        var cancelled = RecordIn(SeparationStatus.Approved);
        typeof(SeparationRecord).GetProperty(nameof(SeparationRecord.IsCancelled))!.SetValue(cancelled, true);
        Assert.True(SeparationWorkflowRules.ReconcileFromLegacyFlags(cancelled, null));
        Assert.Equal(SeparationStatus.Cancelled, cancelled.Status);
    }

    // ── The linked transaction follows the record along its own table (P-4); no row is added to that table.

    [Fact]
    public void The_transaction_follows_a_submission_a_withdrawal_and_a_cancellation_through_InProcess()
    {
        var actor = Guid.NewGuid();
        var transaction = new ServiceTransaction { TransactionType = ServiceTransactionType.Separation };

        SeparationStatusSync.Follow(transaction, SeparationStatus.Draft, SeparationStatus.PendingApproval, actor, null);
        Assert.Equal(ServiceTransactionState.PendingApproval, transaction.State);
        SeparationStatusSync.Follow(transaction, SeparationStatus.PendingApproval, SeparationStatus.Draft, actor, "سحب");
        Assert.Equal(ServiceTransactionState.InProcess, transaction.State);
        SeparationStatusSync.Follow(transaction, SeparationStatus.Draft, SeparationStatus.PendingApproval, actor, null);
        SeparationStatusSync.Follow(transaction, SeparationStatus.PendingApproval, SeparationStatus.Cancelled, actor, "إلغاء");
        Assert.Equal(ServiceTransactionState.Cancelled, transaction.State);

        // Never through Approved: the cancellation did not approve anything.
        Assert.DoesNotContain(transaction.StateHistory, h => h.ToState == ServiceTransactionState.Approved);
        Assert.All(transaction.StateHistory, h => Assert.Equal(actor, h.ChangedBy));
    }

    [Fact]
    public void The_transaction_follows_an_approval_an_execution_an_intervention_and_the_end()
    {
        var transaction = new ServiceTransaction { TransactionType = ServiceTransactionType.Separation };
        SeparationStatusSync.Follow(transaction, SeparationStatus.Draft, SeparationStatus.PendingApproval, Guid.NewGuid(), null);
        SeparationStatusSync.Follow(transaction, SeparationStatus.PendingApproval, SeparationStatus.Approved, Guid.NewGuid(), null);
        SeparationStatusSync.Follow(transaction, SeparationStatus.Approved, SeparationStatus.Executing, Guid.NewGuid(), null);
        Assert.Equal(ServiceTransactionState.Executing, transaction.State);

        var rows = transaction.StateHistory.Count;
        SeparationStatusSync.Follow(transaction, SeparationStatus.Executing, SeparationStatus.AwaitingIntervention, null, "تعثر طرف");
        Assert.Equal(ServiceTransactionState.Executing, transaction.State);
        Assert.Equal(rows + 1, transaction.StateHistory.Count);
        Assert.Contains("بانتظار تدخل", transaction.StateHistory.Last().Reason);

        SeparationStatusSync.Follow(transaction, SeparationStatus.AwaitingIntervention, SeparationStatus.Executing, Guid.NewGuid(), "استئناف");
        SeparationStatusSync.Follow(transaction, SeparationStatus.Executing, SeparationStatus.Completed, null, "اكتمال");
        Assert.Equal(ServiceTransactionState.Archived, transaction.State);
    }

    [Fact]
    public void A_rejection_and_a_reopening_follow_the_transactions_own_table()
    {
        var transaction = new ServiceTransaction { TransactionType = ServiceTransactionType.Separation };
        SeparationStatusSync.Follow(transaction, SeparationStatus.Draft, SeparationStatus.PendingApproval, Guid.NewGuid(), null);
        SeparationStatusSync.Follow(transaction, SeparationStatus.PendingApproval, SeparationStatus.Rejected, Guid.NewGuid(), "سبب");
        Assert.Equal(ServiceTransactionState.Rejected, transaction.State);
        SeparationStatusSync.Follow(transaction, SeparationStatus.Rejected, SeparationStatus.Draft, Guid.NewGuid(), "إعادة فتح");
        Assert.Equal(ServiceTransactionState.InProcess, transaction.State);
    }

    [Fact]
    public void No_row_was_added_to_the_transactions_table_so_other_types_keep_their_paths()
    {
        Assert.False(ServiceTransaction.CanMove(ServiceTransactionState.PendingApproval, ServiceTransactionState.Draft));
        Assert.False(ServiceTransaction.CanMove(ServiceTransactionState.PendingApproval, ServiceTransactionState.Cancelled));
        foreach (var state in Enum.GetValues<ServiceTransactionState>())
            foreach (var target in Enum.GetValues<SeparationStatus>())
            {
                var current = state;
                foreach (var next in SeparationStatusSync.PathTo(state, target))
                {
                    Assert.True(ServiceTransaction.CanMove(current, next), $"{state} → {target}: {current} → {next}");
                    current = next;
                }
            }
    }

    // ── Guards (P-10): the status and its fields change only in the state machine; no code writes ApprovedBy.

    [Theory]
    [InlineData(nameof(SeparationRecord.Status))]
    [InlineData(nameof(SeparationRecord.SeparationEventPublished))]
    [InlineData(nameof(SeparationRecord.IsCancelled))]
    [InlineData(nameof(SeparationRecord.WorkflowInstanceId))]
    [InlineData(nameof(SeparationRecord.SubmittedBy))]
    [InlineData(nameof(SeparationRecord.DecidedBy))]
    [InlineData(nameof(SeparationRecord.ExecutedBy))]
    [InlineData(nameof(SeparationRecord.IsLegacyDraft))]
    [InlineData(nameof(SeparationRecord.ApprovedBy))]
    public void A_field_of_the_state_machine_has_no_public_setter(string property)
    {
        var setter = typeof(SeparationRecord).GetProperty(property)!.GetSetMethod(nonPublic: true)!;
        Assert.False(setter.IsPublic, property);
        Assert.False(setter.IsFamily, property);
    }

    [Fact]
    public void ApprovedBy_has_a_private_setter_and_no_code_writes_it()
    {
        // Private: only SeparationRecord.cs could write it, and it does not (EF reads the column for history).
        Assert.True(typeof(SeparationRecord).GetProperty(nameof(SeparationRecord.ApprovedBy))!.GetSetMethod(true)!.IsPrivate);
        var entity = System.IO.File.ReadAllText(Path.Combine(HRManagement.TestSupport.RepoPaths.Root,
            "src", "Services", "HRContracts", "HR.Contracts.Domain", "Entities", "SeparationRecord.cs"));
        Assert.DoesNotMatch(@"ApprovedBy\s*=[^=>]", entity);
    }

    [Fact]
    public void Inside_the_domain_only_the_state_machine_assigns_a_separations_status()
    {
        var domain = Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "src", "Services", "HRContracts", "HR.Contracts.Domain");
        var offenders = Directory.GetFiles(domain, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f) is not ("SeparationWorkflowRules.cs" or "SeparationRecord.cs"))
            .Where(f =>
            {
                var source = System.IO.File.ReadAllText(f);
                return source.Contains("SeparationRecord", StringComparison.Ordinal)
                       && Regex.IsMatch(source, @"\.(Status|SeparationEventPublished|IsCancelled|WorkflowInstanceId)\s*=[^=>]");
            })
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);

        // Outside the domain the compiler refuses an assignment (internal setters); the state machine's own file writes them in two
        // methods only: Transition and the legacy backfill.
        var rules = System.IO.File.ReadAllText(Path.Combine(domain, "Services", "SeparationWorkflowRules.cs"));
        Assert.Equal(2, Regex.Matches(rules, @"record\.Status = ").Count);
    }

}
