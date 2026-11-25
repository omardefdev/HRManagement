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
        [PayrollRunAct.Reject] = ([PayrollRunStatus.UnderReview], PayrollRunStatus.Calculated),
        [PayrollRunAct.Post] = ([PayrollRunStatus.Approved], PayrollRunStatus.Posted),
        [PayrollRunAct.Reverse] = ([PayrollRunStatus.Posted], PayrollRunStatus.Reversed),
        [PayrollRunAct.Cancel] =
            ([PayrollRunStatus.Draft, PayrollRunStatus.Calculating, PayrollRunStatus.Validating, PayrollRunStatus.Calculated,
              PayrollRunStatus.UnderReview, PayrollRunStatus.Approved], PayrollRunStatus.Cancelled),
    };

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
            case PayrollRunAct.Approve: run.Approve(Guid.NewGuid(), "payroll-approver", "موافق"); break;
            case PayrollRunAct.Reject: run.Reject(Guid.NewGuid(), "الإجمالي لا يطابق كشف المالية"); break;
            case PayrollRunAct.Post: run.Post(); break;
            case PayrollRunAct.Reverse: run.Reverse(); break;
            case PayrollRunAct.Cancel: run.MarkCancelled(); break;
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
            // A recalculation needs results that are not frozen; everything else is indifferent to it or needs them frozen.
            ResultsFrozen = act != PayrollRunAct.BeginCalculating,
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
            Assert.Equal(act != PayrollRunAct.BeginCalculating, run.ResultsFrozen);
            Assert.Equal(approvalsBefore, run.Approvals.Count);
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

    [Fact]
    public void The_tests_helper_sets_a_state_and_exists_in_the_test_projects_only()
    {
        var run = RunIn(PayrollRunStatus.Draft, PayrollRunAct.Cancel);
        ForTesting.SetStatus(run, PayrollRunStatus.Reversed);
        Assert.Equal(PayrollRunStatus.Reversed, run.Status);

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
