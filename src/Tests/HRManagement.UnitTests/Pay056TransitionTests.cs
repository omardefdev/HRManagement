using System.Reflection;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.TestSupport;
using HRManagement.TestSupport.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-56 (G-PAY-06): no payroll run moves between two states without a guard. The matrix of every state × every method of the run
/// is written out here, independently of the table the run reads, and each cell is exercised on a run: an allowed act reaches its
/// state, a refused one throws and leaves the run as it was. And no product code can assign a run's state: the property has no
/// setter after construction, a run is constructed only where this test names, and nothing writes the column around the entity.
/// </summary>
public sealed partial class Pay056TransitionTests
{
    private static readonly PayrollRunStatus[] AllStates =
    [
        PayrollRunStatus.Draft, PayrollRunStatus.Calculating, PayrollRunStatus.Validating, PayrollRunStatus.Calculated,
        PayrollRunStatus.UnderReview, PayrollRunStatus.Approved, PayrollRunStatus.Posted, PayrollRunStatus.Reversed, PayrollRunStatus.Cancelled,
    ];

    /// <summary>The matrix as the plan states it: for each act, the states it may leave and the state it reaches.</summary>
    private static readonly Dictionary<PayrollRunAct, (PayrollRunStatus[] From, PayrollRunStatus To)> Expected = new()
    {
        [PayrollRunAct.BeginCalculating] = ([PayrollRunStatus.Draft, PayrollRunStatus.Calculated, PayrollRunStatus.Calculating], PayrollRunStatus.Calculating),
        [PayrollRunAct.BeginValidating] = ([PayrollRunStatus.Calculating], PayrollRunStatus.Validating),
        [PayrollRunAct.MarkCalculationFailed] = ([PayrollRunStatus.Calculating, PayrollRunStatus.Validating], PayrollRunStatus.Calculating),
        [PayrollRunAct.MarkCalculated] =
            ([PayrollRunStatus.Draft, PayrollRunStatus.Calculated, PayrollRunStatus.Calculating, PayrollRunStatus.Validating], PayrollRunStatus.Calculated),
        [PayrollRunAct.MarkValidated] = ([PayrollRunStatus.Calculating, PayrollRunStatus.Calculated, PayrollRunStatus.Validating], PayrollRunStatus.Calculated),
        [PayrollRunAct.SubmitForReview] = ([PayrollRunStatus.Calculated], PayrollRunStatus.UnderReview),
        [PayrollRunAct.Approve] = ([PayrollRunStatus.UnderReview], PayrollRunStatus.Approved),
        // A rejection reopens the run: a draft again (G-PAY-04). It used to leave the run «محسوب» with frozen results.
        [PayrollRunAct.Reject] = ([PayrollRunStatus.UnderReview], PayrollRunStatus.Draft),
        [PayrollRunAct.Post] = ([PayrollRunStatus.Approved], PayrollRunStatus.Posted),
        [PayrollRunAct.Reverse] = ([PayrollRunStatus.Posted], PayrollRunStatus.Reversed),
        [PayrollRunAct.Cancel] =
            ([PayrollRunStatus.Draft, PayrollRunStatus.Calculating, PayrollRunStatus.Validating, PayrollRunStatus.Calculated,
              PayrollRunStatus.UnderReview, PayrollRunStatus.Approved], PayrollRunStatus.Cancelled),
        [PayrollRunAct.RestartCalculation] =
            ([PayrollRunStatus.Draft, PayrollRunStatus.Calculating, PayrollRunStatus.Validating, PayrollRunStatus.Calculated], PayrollRunStatus.Draft),
    };

    /// <summary>The acts that need results that are not frozen; every other act is indifferent to it or needs them frozen.</summary>
    private static bool NeedsUnfrozen(PayrollRunAct act) => act is PayrollRunAct.BeginCalculating or PayrollRunAct.RestartCalculation;

    private static readonly Guid Actor = Guid.Parse("56000000-0000-4000-8000-0000000000aa");
    private static readonly DateTime When = new(2028, 2, 3, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The method of the run behind each act.</summary>
    private static void Apply(PayrollRun run, PayrollRunAct act)
    {
        switch (act)
        {
            case PayrollRunAct.BeginCalculating: run.BeginCalculating(); break;
            case PayrollRunAct.BeginValidating: run.BeginValidating(); break;
            case PayrollRunAct.MarkCalculationFailed: run.MarkCalculationFailed(); break;
            case PayrollRunAct.MarkCalculated: run.MarkCalculated(900m); break;
            case PayrollRunAct.MarkValidated: run.MarkValidated(900m); break;
            case PayrollRunAct.SubmitForReview: run.SubmitForReview(); break;
            case PayrollRunAct.Approve: run.Approve(Guid.NewGuid(), 1, "payroll-approver", "موافق"); break;
            case PayrollRunAct.Reject: run.ReopenAfterRejection(Actor, When, "الإجمالي لا يطابق كشف المالية"); break;
            case PayrollRunAct.Post: run.Post(Actor, When); break;
            case PayrollRunAct.Reverse: run.Reverse(); break;
            case PayrollRunAct.Cancel: run.Cancel(Actor, When, "أُنشئ التشغيل لفترة خاطئة"); break;
            case PayrollRunAct.RestartCalculation: run.ResetForRestart(); break;
            default: throw new ArgumentOutOfRangeException(nameof(act), act, "an act with no method of the run");
        }
    }

    /// <summary>
    /// A run in <paramref name="state"/> whose other conditions for <paramref name="act"/> hold (a line, a passed validation, frozen
    /// or not as the act needs), so that the state alone decides.
    /// </summary>
    private static PayrollRun RunIn(PayrollRunStatus state, PayrollRunAct act)
    {
        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2028, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            RunDate = new DateTime(2028, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = state,
            TotalNetPay = 900m,
            ValidatedAtCalculate = true,
            ResultsFrozen = !NeedsUnfrozen(act),
        };
        run.SalaryLines.Add(new SalaryLine { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid(), GrossPay = 1000m, TotalDeductions = 100m, NetPay = 900m });
        return run;
    }

    public static TheoryData<PayrollRunStatus, PayrollRunAct> EveryCell()
    {
        var cells = new TheoryData<PayrollRunStatus, PayrollRunAct>();
        foreach (var state in AllStates)
        {
            foreach (var act in Enum.GetValues<PayrollRunAct>())
                cells.Add(state, act);
        }

        return cells;
    }

    [Theory]
    [MemberData(nameof(EveryCell))]
    public void Every_state_and_every_method_is_allowed_or_refused_as_the_matrix_says(PayrollRunStatus state, PayrollRunAct act)
    {
        var (from, to) = Expected[act];
        var run = RunIn(state, act);
        var approvalsBefore = run.Approvals.Count;

        if (from.Contains(state))
        {
            Apply(run, act);
            Assert.Equal(to, run.Status);
        }
        else
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => Apply(run, act));
            Assert.False(string.IsNullOrWhiteSpace(refusal.Message));
            // Refused, and nothing changed: the state, the total, the flags, the decisions.
            Assert.Equal(state, run.Status);
            Assert.Equal(900m, run.TotalNetPay);
            Assert.True(run.ValidatedAtCalculate);
            Assert.Equal(!NeedsUnfrozen(act), run.ResultsFrozen);
            Assert.Equal(approvalsBefore, run.Approvals.Count);
            Assert.Equal(1, run.Revision);
            Assert.Null(run.CancelledBy);
            Assert.Null(run.PostedBy);
        }
    }

    [Fact]
    public void The_table_the_run_reads_is_the_matrix()
    {
        Assert.Equal(Expected.Keys.Order(), PayrollRunTransitions.Rules.Select(r => r.Act).Order());
        Assert.Equal(Enum.GetValues<PayrollRunAct>().Order(), Expected.Keys.Order());
        foreach (var rule in PayrollRunTransitions.Rules)
        {
            Assert.Equal(Expected[rule.Act].From.Order(), rule.From.Order());
            Assert.Equal(Expected[rule.Act].To, rule.To);
            foreach (var state in AllStates)
            {
                Assert.Equal(Expected[rule.Act].From.Contains(state), PayrollRunTransitions.CanApply(rule.Act, state));
                Assert.Equal(Expected[rule.Act].From.Contains(state), PayrollRunTransitions.Refusal(rule.Act, state) is null);
            }
        }
    }

    [Fact]
    public void Every_state_of_the_enum_is_in_the_matrix()
    {
        // The two legacy aliases are the same values as Draft and Calculated.
        Assert.Equal(AllStates.Order(), Enum.GetValues<PayrollRunStatus>().Distinct().Order());
    }

    [Fact]
    public void A_posted_run_leaves_only_by_a_reversal_and_a_reversed_or_cancelled_run_never_leaves()
    {
        var leavingPosted = Enum.GetValues<PayrollRunAct>().Where(a => PayrollRunTransitions.CanApply(a, PayrollRunStatus.Posted));
        Assert.Equal([PayrollRunAct.Reverse], leavingPosted);
        Assert.DoesNotContain(Enum.GetValues<PayrollRunAct>(), a => PayrollRunTransitions.CanApply(a, PayrollRunStatus.Reversed));
        Assert.DoesNotContain(Enum.GetValues<PayrollRunAct>(), a => PayrollRunTransitions.CanApply(a, PayrollRunStatus.Cancelled));
    }

    [Theory]
    [InlineData(PayrollRunStatus.UnderReview)]
    [InlineData(PayrollRunStatus.Approved)]
    [InlineData(PayrollRunStatus.Posted)]
    [InlineData(PayrollRunStatus.Reversed)]
    [InlineData(PayrollRunStatus.Cancelled)]
    [InlineData(PayrollRunStatus.Draft)]
    public void A_validation_never_brings_a_run_back_to_calculated(PayrollRunStatus state)
    {
        // G-PAY-06: MarkValidated used to set «محسوب» from any state, so a validation revived an approved, posted or reversed run.
        var run = RunIn(state, PayrollRunAct.MarkValidated);

        Assert.Throws<InvalidOperationException>(() => run.MarkValidated(1m));
        Assert.Equal(state, run.Status);
        Assert.Equal(900m, run.TotalNetPay);
    }

    [Fact]
    public void A_frozen_run_is_not_calculated_again_whatever_its_state()
    {
        foreach (var state in AllStates)
        {
            var run = RunIn(state, PayrollRunAct.Post); // frozen
            Assert.Throws<InvalidOperationException>(run.BeginCalculating);
            Assert.Equal(state, run.Status);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_rejection_needs_its_note_and_a_cancellation_its_reason(string? text)
    {
        var underReview = RunIn(PayrollRunStatus.UnderReview, PayrollRunAct.Reject);
        Assert.Equal(PayrollRun.RejectionNoteRequired,
            Assert.Throws<InvalidOperationException>(() => underReview.ReopenAfterRejection(Actor, When, text)).Message);
        Assert.Equal(PayrollRunStatus.UnderReview, underReview.Status);
        Assert.Equal(1, underReview.Revision);
        Assert.True(underReview.ResultsFrozen);

        var calculated = RunIn(PayrollRunStatus.Calculated, PayrollRunAct.Cancel);
        Assert.Equal(PayrollRun.CancellationReasonRequired,
            Assert.Throws<InvalidOperationException>(() => calculated.Cancel(Actor, When, text)).Message);
        Assert.Equal(PayrollRunStatus.Calculated, calculated.Status);
        Assert.Null(calculated.CancelledBy);
        Assert.Empty(calculated.Approvals);
    }

    [Fact]
    public void A_rejection_reopens_the_run_and_a_cancellation_names_its_actor_and_reason()
    {
        var run = RunIn(PayrollRunStatus.UnderReview, PayrollRunAct.Reject);
        var rejection = run.ReopenAfterRejection(Actor, When, "  يُصحَّح بدل النقل  ");

        Assert.Equal(PayrollRunStatus.Draft, run.Status);
        Assert.Equal(2, run.Revision);
        Assert.False(run.ResultsFrozen);
        Assert.False(run.ValidatedAtCalculate);
        Assert.Equal(0m, run.TotalNetPay);
        Assert.Equal((ApprovalDecision.Rejected, "يُصحَّح بدل النقل", Actor, When), (rejection.Decision, rejection.Notes, rejection.ApproverId, rejection.DecidedAt));
        // A reopened run is calculated again: it is a draft whose results are not frozen.
        run.BeginCalculating();
        Assert.Equal(PayrollRunStatus.Calculating, run.Status);

        var cancelled = RunIn(PayrollRunStatus.Approved, PayrollRunAct.Cancel);
        var cancellation = cancelled.Cancel(Actor, When, "أُنشئ لفترة خاطئة");
        Assert.Equal((PayrollRunStatus.Cancelled, (Guid?)Actor, (DateTime?)When, "أُنشئ لفترة خاطئة"),
            (cancelled.Status, cancelled.CancelledBy, cancelled.CancelledAt, cancelled.CancellationReason));
        Assert.Equal((ApprovalDecision.Cancelled, "أُنشئ لفترة خاطئة", Actor), (cancellation.Decision, cancellation.Notes, cancellation.ApproverId));
    }

    [Fact]
    public void A_posting_names_its_poster_and_a_restart_needs_results_that_are_not_frozen()
    {
        var approved = RunIn(PayrollRunStatus.Approved, PayrollRunAct.Post);
        approved.Post(Actor, When);
        Assert.Equal(((Guid?)Actor, (DateTime?)When, 900m), (approved.PostedBy, approved.PostedAt, approved.TotalNetPay));

        var frozen = RunIn(PayrollRunStatus.Calculated, PayrollRunAct.Post);
        Assert.Equal(PayrollRun.RestartNeedsUnfrozenResults, Assert.Throws<InvalidOperationException>(frozen.ResetForRestart).Message);
        Assert.Equal(PayrollRunStatus.Calculated, frozen.Status);

        var failed = RunIn(PayrollRunStatus.Calculating, PayrollRunAct.RestartCalculation);
        failed.ResetForRestart();
        Assert.Equal((PayrollRunStatus.Draft, 0m, false), (failed.Status, failed.TotalNetPay, failed.ValidatedAtCalculate));
    }

    // --- nothing sets the state after construction ---

    [Fact]
    public void The_state_has_no_setter_after_construction()
    {
        var setter = typeof(PayrollRun).GetProperty(nameof(PayrollRun.Status))!.SetMethod;

        Assert.NotNull(setter);
        Assert.Contains(
            setter!.ReturnParameter.GetRequiredCustomModifiers(),
            modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }

    [Theory]
    [InlineData(nameof(PayrollRun.ResultsFrozen))]
    [InlineData(nameof(PayrollRun.ValidatedAtCalculate))]
    public void Nothing_freezes_or_validates_a_run_by_hand(string property)
    {
        // The correction run used to set both after construction, around the validation. They are given at construction (a seeded
        // run) and changed by the run's own methods only.
        var setter = typeof(PayrollRun).GetProperty(property)!.SetMethod;

        Assert.NotNull(setter);
        Assert.Contains(
            setter!.ReturnParameter.GetRequiredCustomModifiers(),
            modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }

    [Fact]
    public void The_tests_helper_sets_a_state_and_exists_in_the_test_projects_only()
    {
        var run = RunIn(PayrollRunStatus.Draft, PayrollRunAct.Cancel);
        ForTesting.SetStatus(run, PayrollRunStatus.Reversed);
        Assert.Equal(PayrollRunStatus.Reversed, run.Status);
        var frozen = RunIn(PayrollRunStatus.Calculated, PayrollRunAct.Post);
        ForTesting.Unfreeze(frozen);
        Assert.False(frozen.ResultsFrozen);
        Assert.False(frozen.ValidatedAtCalculate);

        var users = ProductSources().Where(s => s.Text.Contains("ForTesting", StringComparison.Ordinal)).Select(s => s.Relative).ToList();
        Assert.Empty(users);
    }

    /// <summary>The product files that construct a run, each with why. A run is created in its state there, not moved to it.</summary>
    public static readonly IReadOnlyDictionary<string, string> Constructors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs"] = "CreatePayrollRunHandler: a new draft",
        ["Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs"] = "CreateCorrectionRunHandler: a new draft of a correction",
        ["Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs"] = "the demo seed: a run created in its seeded state",
        ["Services/Payroll/Payroll.Infrastructure/Data/AcceptancePayrollSeeder.cs"] = "the acceptance seed: one run per state, each created in it",
    };

    [GeneratedRegex(@"new\s+PayrollRun\s*(\(|\{|$)", RegexOptions.Multiline)]
    private static partial Regex Construction();

    /// <summary>
    /// A write of a run's state around the entity: an assignment of a <c>PayrollRunStatus</c> to a <c>Status</c> member (outside a
    /// construction), a bulk update's <c>SetProperty</c> of it, the backing field, or SQL that updates the runs' table.
    /// </summary>
    [GeneratedRegex(
        @"(\.Status\s*=(?!=)\s*PayrollRunStatus\.)|(SetProperty\([^)]*Status\s*,\s*PayrollRunStatus\.)|(\b_status\b)|(UPDATE\s+\\?""PayrollRuns\\?"")",
        RegexOptions.IgnoreCase)]
    private static partial Regex WriteAroundTheEntity();

    private const string TheRun = "Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs";

    private static IEnumerable<(string Relative, string Text)> ProductSources()
    {
        var src = Path.Combine(RepoPaths.Root, "src");
        foreach (var root in new[] { "Services", "Shared", "Gateway" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(src, root), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)
                    || relative.Contains("/Migrations/", StringComparison.Ordinal))
                    continue;
                yield return (relative, System.IO.File.ReadAllText(file));
            }
        }
    }

    [Fact]
    public void A_run_is_constructed_only_where_this_test_names()
    {
        var found = ProductSources().Where(s => Construction().IsMatch(s.Text)).Select(s => s.Relative).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(Constructors.Keys.Order(StringComparer.Ordinal), found);
    }

    [Fact]
    public void No_product_code_writes_a_runs_state_around_the_entity()
    {
        var found = ProductSources()
            .Where(s => s.Relative != TheRun && s.Relative.StartsWith("Services/Payroll/", StringComparison.Ordinal))
            .Where(s => WriteAroundTheEntity().IsMatch(s.Text))
            .Select(s => s.Relative)
            .ToList();

        Assert.Empty(found);
        // The entity itself changes its state in one place: Move, through the transitions table.
        var entity = ProductSources().Single(s => s.Relative == TheRun).Text;
        Assert.Single(Regex.Matches(entity, @"_status\s*=\s*PayrollRunTransitions\.EnsureTransition\("));
        Assert.Equal(2, Regex.Matches(entity, @"_status\s*=(?!=)").Count); // Move, and the init accessor
    }

    [Fact]
    public void The_scan_sees_a_planted_write_and_a_planted_construction()
    {
        Assert.Matches(WriteAroundTheEntity(), "finalRun.Status = PayrollRunStatus.Calculating;");
        Assert.Matches(WriteAroundTheEntity(), "await q.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, PayrollRunStatus.Cancelled));");
        Assert.Matches(WriteAroundTheEntity(), "typeof(PayrollRun).GetField(\"_status\", flags)");
        Assert.Matches(WriteAroundTheEntity(), "UPDATE \\\"PayrollRuns\\\" SET \\\"Status\\\" = 8");
        Assert.Matches(WriteAroundTheEntity(), "UPDATE \"PayrollRuns\" SET \"Status\" = 8");
        Assert.DoesNotMatch(WriteAroundTheEntity(), "if (run.Status == PayrollRunStatus.Posted) return;");
        Assert.DoesNotMatch(WriteAroundTheEntity(), "batch.Status = PaymentBatchStatus.Submitted;");
        Assert.DoesNotMatch(WriteAroundTheEntity(), "query.Where(r => r.Status == status.Value)");
        Assert.Matches(Construction(), "var run = new PayrollRun { Status = PayrollRunStatus.Posted };");
        Assert.Matches(Construction(), "db.PayrollRuns.Add(new PayrollRun\n{");
        Assert.DoesNotMatch(Construction(), "new PayrollRunJob { }");
        Assert.DoesNotMatch(Construction(), "new PayrollRunApproval()");
    }

    // --- the duplicate bank reference ---

    [Fact]
    public void A_duplicate_bank_reference_of_a_runs_batch_is_answered_in_arabic_as_a_conflict()
    {
        var resolution = UniqueConstraintMessageRegistry.Resolve(constraintName: "IX_PaymentBatches_Run_BankRef");

        Assert.Equal(UniqueConstraintMessageRegistry.MessageForPaymentBatchReference, resolution.Message);
        Assert.NotEqual(ConcurrencyConflictMessages.UniqueViolation, resolution.Message);
        Assert.Contains("المرجع المصرفي", resolution.Message);
    }
}
