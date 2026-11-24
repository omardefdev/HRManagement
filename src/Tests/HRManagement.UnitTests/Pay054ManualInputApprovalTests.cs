using System.Text.RegularExpressions;
using HRManagement.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Moq;
using Payroll.Application.Features.ManualInputs;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using H = HRManagement.UnitTests.Pay054FrameworkHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-54 (§4, D-PAY-6, P-9): a manual allowance, deduction or adjustment counts only after a second person approves it. It is
/// entered pending, of origin Manual, with who entered it and nothing the client may not say; the person who entered it never decides
/// it; a rejection needs a reason; and every reader that feeds pay takes approved inputs only. What the system writes (the event
/// consumers, the seeders, every row older than this plan) is approved and of the system, with no code of its own.
/// </summary>
public sealed class Pay054ManualInputApprovalTests
{
    private static readonly Guid Employee = Guid.Parse("54000000-0000-4000-8000-0000000000e1");
    private static readonly Guid Maker = H.Editor;
    private static readonly Guid Checker = H.Approver;

    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(name);
        return environment.Object;
    }

    private static async Task<(H Harness, Guid AllowanceType)> SeedAsync()
    {
        var h = new H();
        await using var db = h.Db();
        var type = new CompensationType { Code = "ALL-NAT", NameAr = "بدل طبيعة عمل" };
        db.CompensationTypes.Add(type);
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            EmployeeId = Employee,
            BaseSalary = 100_000m,
            IsActive = true,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
        return (h, type.Id);
    }

    private static async Task<ManualInputResult> CreateAsync(
        H h, ManualInputKind kind, Guid? allowanceType = null, decimal amount = 1_500m, string? reason = "قرار الوزير 12 لعام 2026",
        Guid? actor = null, Guid? employee = null, bool requireApproval = true, string environment = "Production", DateOnly? from = null, DateOnly? to = null)
    {
        await using var db = h.Db();
        return await new CreateManualInputHandler(
            h.Repository(db),
            Microsoft.Extensions.Options.Options.Create(new PayrollManualInputOptions { RequireApproval = requireApproval }),
            Environment(environment)).Handle(
            new CreateManualInputCommand(kind, employee ?? Employee, amount, reason, actor ?? Maker,
                AllowanceTypeId: allowanceType, DeductionType: kind == ManualInputKind.Deduction ? "Other" : null,
                EffectiveFrom: kind == ManualInputKind.Allowance ? from ?? new DateOnly(2026, 10, 1) : null, EffectiveTo: to),
            CancellationToken.None);
    }

    private static async Task<ManualInputResult> DecideAsync(H h, ManualInputKind kind, Guid id, ManualInputDecision decision, string? note = null, Guid? actor = null)
    {
        await using var db = h.Db();
        return await new DecideManualInputHandler(h.Repository(db), h.Calendar).Handle(
            new DecideManualInputCommand(kind, id, decision, note, actor ?? Checker), CancellationToken.None);
    }

    private static async Task<IManualPayInput> InputAsync(H h, ManualInputKind kind, Guid id)
    {
        await using var db = h.Db();
        return kind switch
        {
            ManualInputKind.Allowance => await db.AllowanceRecords.AsNoTracking().SingleAsync(x => x.Id == id),
            ManualInputKind.Deduction => await db.DeductionRecords.AsNoTracking().SingleAsync(x => x.Id == id),
            _ => await db.PayrollAdjustments.AsNoTracking().SingleAsync(x => x.Id == id),
        };
    }

    // ------------------------------------------------------------------ create

    [Theory]
    [InlineData(ManualInputKind.Allowance)]
    [InlineData(ManualInputKind.Deduction)]
    [InlineData(ManualInputKind.Adjustment)]
    public async Task A_manual_input_is_entered_pending_of_origin_manual_with_who_entered_it_and_no_run(ManualInputKind kind)
    {
        var (h, type) = await SeedAsync();
        using var _ = h;

        var result = await CreateAsync(h, kind, type);

        Assert.True(result.Success, result.Error);
        Assert.Equal(ManualInputApprovalStatus.PendingApproval, result.Status);
        var input = await InputAsync(h, kind, result.InputId!.Value);
        Assert.Equal(ManualInputApprovalStatus.PendingApproval, input.ApprovalStatus);
        Assert.Equal(ManualInputOrigin.Manual, input.Origin);
        Assert.Equal(Maker, input.CreatedBy);
        Assert.Null(input.ApprovedBy);
        Assert.Null(input.ApprovedAt);
        Assert.Equal(Employee, input.EmployeeId);
        switch (input)
        {
            case AllowanceRecord a:
                Assert.Equal((1_500m, "قرار الوزير 12 لعام 2026", type), (a.Amount, a.Reason, a.AllowanceTypeId));
                Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), a.EffectiveFrom);
                break;
            case DeductionRecord d:
                Assert.Equal((1_500m, "قرار الوزير 12 لعام 2026", "Other"), (d.Amount, d.Reason, d.DeductionType));
                Assert.Null(d.AppliedInRunId);
                Assert.Null(d.SourceReference);
                break;
            case PayrollAdjustment p:
                Assert.Equal((1_500m, "قرار الوزير 12 لعام 2026"), (p.Amount, p.AdjustmentReason));
                Assert.Null(p.PayrollRunId);
                break;
        }
    }

    /// <summary>The command has no field for the run an input applies to, its state, its origin or its approver: a client cannot say them.</summary>
    [Fact]
    public void The_create_command_and_the_forms_carry_nothing_of_the_run_or_of_the_approval()
    {
        string[] forbidden = ["AppliedInRunId", "PayrollRunId", "ApprovalStatus", "Origin", "ApprovedBy", "ApprovedAt", "DecisionNote", "CreatedBy", "SourceReference", "TenantId", "Id"];
        var command = typeof(CreateManualInputCommand).GetProperties().Select(p => p.Name).ToList();
        Assert.All(forbidden, name => Assert.DoesNotContain(name, command));
    }

    [Fact]
    public async Task A_manual_input_needs_an_employee_with_a_pay_profile_a_reason_and_an_amount_and_a_refusal_writes_nothing()
    {
        var (h, type) = await SeedAsync();
        using var _ = h;

        async Task RefusedAsync(Task<ManualInputResult> act, string error)
        {
            var result = await act;
            Assert.False(result.Success);
            Assert.Equal(400, result.StatusCode);
            Assert.Equal(error, result.Error);
        }

        await RefusedAsync(CreateAsync(h, ManualInputKind.Deduction, reason: " "), ManualInputMessages.ReasonRequired);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Deduction, reason: new string('س', 501)), ManualInputMessages.ReasonTooLong);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Deduction, amount: 0m), ManualInputMessages.AmountPositive);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Allowance, type, amount: -5m), ManualInputMessages.AmountPositive);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Adjustment, amount: 0m), ManualInputMessages.AdjustmentNotZero);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Adjustment, amount: 10.125m), ManualInputMessages.AmountScale);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Adjustment, employee: Guid.NewGuid()), ManualInputMessages.NoPayProfile);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Adjustment, employee: Guid.Empty), ManualInputMessages.EmployeeRequired);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Adjustment, actor: Guid.Empty), ManualInputMessages.UnknownActor);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Allowance, allowanceType: Guid.NewGuid()), ManualInputMessages.AllowanceTypeRequired);
        await RefusedAsync(CreateAsync(h, ManualInputKind.Allowance, type, from: new DateOnly(2026, 10, 1), to: new DateOnly(2026, 9, 1)), ManualInputMessages.EffectiveToBeforeFrom);

        await using var db = h.Db();
        Assert.Equal(0, await db.AllowanceRecords.CountAsync() + await db.DeductionRecords.CountAsync() + await db.PayrollAdjustments.CountAsync());
        // A negative adjustment is a real adjustment.
        Assert.True((await CreateAsync(h, ManualInputKind.Adjustment, amount: -250m)).Success);
    }

    // ------------------------------------------------------------------ decide

    [Theory]
    [InlineData(ManualInputKind.Allowance)]
    [InlineData(ManualInputKind.Deduction)]
    [InlineData(ManualInputKind.Adjustment)]
    public async Task The_person_who_entered_an_input_never_decides_it_and_a_second_person_approves_it(ManualInputKind kind)
    {
        var (h, type) = await SeedAsync();
        using var _ = h;
        var id = (await CreateAsync(h, kind, type)).InputId!.Value;

        foreach (var decision in new[] { ManualInputDecision.Approved, ManualInputDecision.Rejected })
        {
            var own = await DecideAsync(h, kind, id, decision, "ملاحظة", actor: Maker);
            Assert.False(own.Success);
            Assert.Equal(ManualInputFailure.SelfDecision, own.Failure);
            Assert.Equal(403, own.StatusCode);
            Assert.Equal(ManualInputMessages.SelfDecision, own.Error);
            Assert.Equal(ManualInputApprovalStatus.PendingApproval, own.Status);
        }

        Assert.Equal(ManualInputApprovalStatus.PendingApproval, (await InputAsync(h, kind, id)).ApprovalStatus);

        var approved = await DecideAsync(h, kind, id, ManualInputDecision.Approved, actor: Checker);
        Assert.True(approved.Success, approved.Error);
        var input = await InputAsync(h, kind, id);
        Assert.Equal(ManualInputApprovalStatus.Approved, input.ApprovalStatus);
        Assert.Equal(Checker, input.ApprovedBy);
        Assert.Equal(h.Clock.Now.UtcDateTime, input.ApprovedAt);
        Assert.Equal(Maker, input.CreatedBy);
        Assert.Equal(ManualInputOrigin.Manual, input.Origin);

        // A decision is not taken twice.
        var again = await DecideAsync(h, kind, id, ManualInputDecision.Rejected, "تراجع", actor: H.Attestor);
        Assert.Equal(ManualInputFailure.Conflict, again.Failure);
        Assert.Equal(409, again.StatusCode);
        Assert.Equal(ManualInputApprovalStatus.Approved, (await InputAsync(h, kind, id)).ApprovalStatus);
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_and_keeps_it_and_a_rejected_input_stays_rejected()
    {
        var (h, type) = await SeedAsync();
        using var _ = h;
        var id = (await CreateAsync(h, ManualInputKind.Allowance, type)).InputId!.Value;

        var bare = await DecideAsync(h, ManualInputKind.Allowance, id, ManualInputDecision.Rejected, " ");
        Assert.Equal((false, 400, ManualInputMessages.RejectNoteRequired), (bare.Success, bare.StatusCode, bare.Error));
        var tooLong = await DecideAsync(h, ManualInputKind.Allowance, id, ManualInputDecision.Rejected, new string('س', 501));
        Assert.Equal(ManualInputMessages.NoteTooLong, tooLong.Error);
        Assert.Equal(ManualInputApprovalStatus.PendingApproval, (await InputAsync(h, ManualInputKind.Allowance, id)).ApprovalStatus);

        var rejected = await DecideAsync(h, ManualInputKind.Allowance, id, ManualInputDecision.Rejected, "لا سند قانوني للبدل");
        Assert.True(rejected.Success, rejected.Error);
        var input = await InputAsync(h, ManualInputKind.Allowance, id);
        Assert.Equal((ManualInputApprovalStatus.Rejected, "لا سند قانوني للبدل", Checker), (input.ApprovalStatus, input.DecisionNote, input.ApprovedBy));
        Assert.Equal(ManualInputFailure.Conflict, (await DecideAsync(h, ManualInputKind.Allowance, id, ManualInputDecision.Approved)).Failure);
    }

    [Fact]
    public async Task A_row_the_system_wrote_is_never_decided_and_an_unknown_input_is_not_found()
    {
        var (h, _) = await SeedAsync();
        using var __ = h;
        Guid system;
        await using (var db = h.Db())
        {
            // As an event consumer writes it: no approval field is set by anyone.
            var row = new PayrollAdjustment { EmployeeId = Employee, Amount = 0m, AdjustmentReason = "نقل — TransferExecuted" };
            db.PayrollAdjustments.Add(row);
            await db.SaveChangesAsync();
            system = row.Id;
        }

        var decided = await DecideAsync(h, ManualInputKind.Adjustment, system, ManualInputDecision.Rejected, "ملاحظة");
        Assert.Equal(ManualInputFailure.Conflict, decided.Failure);
        Assert.Equal(ManualInputMessages.NotPending, decided.Error);
        Assert.Equal(ManualInputApprovalStatus.Approved, (await InputAsync(h, ManualInputKind.Adjustment, system)).ApprovalStatus);
        Assert.Equal(ManualInputFailure.NotFound, (await DecideAsync(h, ManualInputKind.Deduction, Guid.NewGuid(), ManualInputDecision.Approved)).Failure);
        Assert.Equal(404, (await DecideAsync(h, ManualInputKind.Deduction, system, ManualInputDecision.Approved)).StatusCode);   // another kind's id
    }

    // ------------------------------------------------------------------ the readers that feed pay

    [Fact]
    public async Task The_loader_gives_a_run_approved_inputs_only_pending_and_rejected_ones_are_absent()
    {
        var (h, type) = await SeedAsync();
        using var _ = h;
        var kinds = new[] { ManualInputKind.Allowance, ManualInputKind.Deduction, ManualInputKind.Adjustment };
        var pending = new Dictionary<ManualInputKind, Guid>();
        var approved = new Dictionary<ManualInputKind, Guid>();
        var rejected = new Dictionary<ManualInputKind, Guid>();
        foreach (var kind in kinds)
        {
            pending[kind] = (await CreateAsync(h, kind, type, amount: 111m)).InputId!.Value;
            approved[kind] = (await CreateAsync(h, kind, type, amount: 222m)).InputId!.Value;
            rejected[kind] = (await CreateAsync(h, kind, type, amount: 333m)).InputId!.Value;
            Assert.True((await DecideAsync(h, kind, approved[kind], ManualInputDecision.Approved)).Success);
            Assert.True((await DecideAsync(h, kind, rejected[kind], ManualInputDecision.Rejected, "مرفوض")).Success);
        }

        await using (var db = h.Db())
        {
            // And one row of each kind as the system writes it.
            db.AllowanceRecords.Add(new AllowanceRecord { EmployeeId = Employee, AllowanceTypeId = type, Amount = 444m, EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            db.DeductionRecords.Add(new DeductionRecord { EmployeeId = Employee, DeductionType = "AdvanceRecovery", Amount = 444m });
            db.PayrollAdjustments.Add(new PayrollAdjustment { EmployeeId = Employee, Amount = 444m, AdjustmentReason = "مهمة" });
            await db.SaveChangesAsync();
        }

        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
        };
        await using var read = h.Db();
        var bag = await PayrollPartitionInputLoader.LoadAsync(
            h.Repository(read), [Employee], run, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), CancellationToken.None);

        Assert.Equal([222m, 444m], bag.AllowancesFor(Employee).Select(x => x.Amount).Order());
        Assert.Equal([222m, 444m], bag.DeductionsFor(Employee).Select(x => x.Amount).Order());
        Assert.Equal([222m, 444m], bag.AdjustmentsFor(Employee).Select(x => x.Amount).Order());
        Assert.All(bag.AllowancesFor(Employee), x => Assert.Equal(ManualInputApprovalStatus.Approved, x.ApprovalStatus));
        Assert.DoesNotContain(bag.AllowancesFor(Employee), x => x.Id == pending[ManualInputKind.Allowance] || x.Id == rejected[ManualInputKind.Allowance]);
        Assert.DoesNotContain(bag.DeductionsFor(Employee), x => x.Id == pending[ManualInputKind.Deduction] || x.Id == rejected[ManualInputKind.Deduction]);
        Assert.DoesNotContain(bag.AdjustmentsFor(Employee), x => x.Id == pending[ManualInputKind.Adjustment] || x.Id == rejected[ManualInputKind.Adjustment]);

        // Approved later, a pending input is in the next load (rule 1: the next run takes it).
        Assert.True((await DecideAsync(h, ManualInputKind.Allowance, pending[ManualInputKind.Allowance], ManualInputDecision.Approved)).Success);
        await using var later = h.Db();
        var next = await PayrollPartitionInputLoader.LoadAsync(
            h.Repository(later), [Employee], run, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), CancellationToken.None);
        Assert.Equal([111m, 222m, 444m], next.AllowancesFor(Employee).Select(x => x.Amount).Order());
    }

    // ------------------------------------------------------------------ what the system writes

    [Fact]
    public void A_row_nobody_gave_an_approval_state_is_approved_and_of_the_system()
    {
        foreach (IManualPayInput input in new IManualPayInput[] { new AllowanceRecord(), new DeductionRecord(), new PayrollAdjustment() })
        {
            Assert.Equal(ManualInputApprovalStatus.Approved, input.ApprovalStatus);
            Assert.Equal(ManualInputOrigin.System, input.Origin);
            Assert.Null(input.ApprovedBy);
        }

        // The defaults are the first members: a database default and a CLR default say the same.
        Assert.Equal(0, (int)ManualInputApprovalStatus.Approved);
        Assert.Equal(0, (int)ManualInputOrigin.System);
    }

    /// <summary>
    /// The consumers and the seeders keep writing inputs as before, and so write them approved and of the system: no product file
    /// but the manual input commands sets an input's approval state or origin.
    /// </summary>
    [Fact]
    public void Only_the_manual_input_commands_set_an_inputs_approval_state_or_origin()
    {
        var setter = new Regex(@"\b(ApprovalStatus|Origin)\s*=(?!=)\s*ManualInput(ApprovalStatus|Origin)\.", RegexOptions.CultureInvariant);
        var root = Path.Combine(RepoPaths.Root, "src", "Services");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/Migrations/", StringComparison.Ordinal))
            .Where(f => setter.IsMatch(System.IO.File.ReadAllText(Path.Combine(root, f))))
            .Order(StringComparer.Ordinal)
            .ToList();

        // (The entities' own defaults, Approved and System, are property initialisers and are checked by the test above.)
        Assert.Equal(["Payroll/Payroll.Application/Features/ManualInputs/ManualInputCommands.cs"], offenders);
        Assert.Matches(setter, "entity.ApprovalStatus = ManualInputApprovalStatus.Approved;");
        Assert.Matches(setter, "Origin = ManualInputOrigin.Manual,");
        Assert.DoesNotMatch(setter, "if (x.ApprovalStatus == ManualInputApprovalStatus.Approved)");
    }

    // ------------------------------------------------------------------ the setting

    [Theory]
    [InlineData(true, "Development", true, false)]
    [InlineData(false, "Development", false, false)]      // honoured in Development
    [InlineData(true, "Production", true, false)]
    [InlineData(false, "Production", true, true)]         // ignored, with a warning at start
    [InlineData(false, "Staging", true, true)]
    [InlineData(false, "Acceptance", true, true)]
    public async Task Skipping_the_second_person_is_honoured_in_development_only(bool setting, string environment, bool required, bool ignored)
    {
        var options = new PayrollManualInputOptions { RequireApproval = setting };
        Assert.Equal(required, ManualInputApproval.IsRequired(options, Environment(environment)));
        Assert.Equal(ignored, ManualInputApproval.IsIgnored(options, Environment(environment)));
        Assert.True(ManualInputApproval.IsRequired(options, null) || setting);                 // no environment known: required
        Assert.True(new PayrollManualInputOptions().RequireApproval);                           // the default

        var (h, _) = await SeedAsync();
        using var __ = h;
        var created = await CreateAsync(h, ManualInputKind.Adjustment, requireApproval: setting, environment: environment);
        Assert.Equal(required ? ManualInputApprovalStatus.PendingApproval : ManualInputApprovalStatus.Approved, created.Status);
        Assert.Equal(ManualInputOrigin.Manual, (await InputAsync(h, ManualInputKind.Adjustment, created.InputId!.Value)).Origin);
    }
}
