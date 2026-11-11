using FluentValidation;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Application.Features.LeaveRequests;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Policy;
using HRManagement.TestSupport.Policy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49 (G-POL-04, D-POL-14): leave passes the policy engine on every channel: a request HR records, an edit, the approval,
/// and a request that arrives from the self-service portal. Each is asked once, with the facts of the leave domain's schema and
/// nothing else, after the local validation. In Shadow the engine's refusal changes nothing; in Enforce it is a validation
/// error on the page, a refused approval, and, for the portal's request, a request created Rejected with the reason (never
/// dropped). The engine is a fake that records every question.
/// </summary>
public sealed class Pol049LeaveChannelsTests
{
    private const string PolicyReason = "مرفوض بقاعدة اختبار";
    private static readonly string[] LeaveSchema = ["action", "durationDays", "isSelfApproval", "leaveType"];

    // 2026-09-01 is a Tuesday: Tuesday to Saturday holds three working days (Friday and Saturday do not count).
    private static readonly DateOnly Tuesday = new(2026, 9, 1);
    private static readonly DateOnly Saturday = new(2026, 9, 5);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ContractsDbContext Db { get; }
        public ContractsRepository Repository { get; }
        public FakePolicyEnforceClient Policy { get; } = new();
        public Guid Employee { get; } = Guid.NewGuid();
        public Guid Maker { get; } = Guid.NewGuid();
        public Guid Checker { get; } = Guid.NewGuid();

        public Fixture()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            Db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseSqlite(_connection).Options);
            Db.Database.EnsureCreated();
            Repository = new ContractsRepository(Db);
        }

        public CreateLeaveRequestHandler Create => new(Repository, Policy, CodeGenerationTestSupport.CreateClient());
        public UpdateLeaveRequestHandler Update => new(Repository, Policy);
        public ApproveLeaveRequestHandler Approve => new(Repository, Mock.Of<IEventPublisher>(), Policy);
        public ProcessLeaveRequestedHandler SelfService => new(Repository, CodeGenerationTestSupport.CreateClient(), Policy);

        /// <summary>Shadow, as the real client answers it: the caller is allowed, the server's refusal is on the side.</summary>
        public void ShadowWhereTheServerRefuses() =>
            Policy.EnforceHandler = _ => FakePolicyEnforceClient.Shadow(
                new PolicyDecision { Allowed = true },
                new PolicyDecision { Allowed = false, DenyReason = PolicyReason });

        /// <summary>Enforce, where the policy refuses what <paramref name="when"/> selects (everything by default).</summary>
        public void EnforceRefusing(Func<PolicyEnforceRequest, bool>? when = null) =>
            Policy.EnforceHandler = request => when is null || when(request)
                ? new PolicyEnforceResponse
                {
                    Allowed = false,
                    EffectiveMode = PolicyEnforcementMode.Enforce,
                    Decision = new PolicyDecision { Allowed = false, DenyReason = PolicyReason }
                }
                : new PolicyEnforceResponse { Allowed = true, EffectiveMode = PolicyEnforcementMode.Enforce, Decision = new PolicyDecision { Allowed = true } };

        public LeaveRequestDto Dto(DateOnly start, DateOnly end, LeaveType type = LeaveType.Annual, string? notes = null) =>
            new(null, Employee, type, start, end, 0, ApprovalStatus.Pending, null, notes);

        /// <summary>A leave with its service transaction, as the handlers leave them.</summary>
        public async Task<LeaveRequest> SeedLeaveAsync(ApprovalStatus status, DateOnly start, DateOnly end, Guid? createdBy = null, bool completedTransaction = false)
        {
            var transaction = new ServiceTransaction { EmployeeId = Employee, TransactionType = ServiceTransactionType.LeaveAdvance };
            if (completedTransaction)
                ServiceTransactionWorkflowHelper.AdvanceToCompleted(transaction, Guid.NewGuid(), "seed");
            var leave = new LeaveRequest
            {
                EmployeeId = Employee,
                LeaveType = LeaveType.Annual,
                StartDate = start,
                EndDate = end,
                DurationDays = LeaveAndAdvanceRules.ComputeLeaveDurationDays(start, end),
                Status = status,
                CreatedBy = createdBy ?? Maker
            };
            Db.ServiceTransactions.Add(transaction);
            Db.LeaveRequests.Add(leave);
            await Db.SaveChangesAsync();
            leave.ServiceTransactionId = transaction.Id;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return leave;
        }

        public async Task SeedBalanceAsync(int entitlement = 30)
        {
            Db.LeaveBalances.Add(new LeaveBalance { EmployeeId = Employee, LeaveType = LeaveType.Annual, Year = Tuesday.Year, TotalEntitlement = entitlement });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private static void AssertLeaveQuestion(PolicyEnforceRequest asked, string action, Guid leaveId, int durationDays, string leaveType)
    {
        Assert.Equal(PolicyDomains.ContractsLeave, asked.Domain);
        Assert.Equal("LeaveRequest", asked.SubjectType);
        Assert.Equal(action, asked.Action);
        Assert.Equal(leaveId, asked.EntityId);
        Assert.Equal(action, asked.Facts["action"]);
        Assert.Equal(durationDays.ToString(System.Globalization.CultureInfo.InvariantCulture), asked.Facts["durationDays"]);
        Assert.Equal(leaveType, asked.Facts["leaveType"]);
        // Only fields of the leave schema, and the catalog lists the pair.
        Assert.All(asked.Facts.Keys, key => Assert.Contains(key, LeaveSchema));
        Assert.True(PolicyDomainCatalog.Find(PolicyDomains.ContractsLeave)!.AllowsAction(asked.SubjectType, asked.Action));
        Assert.Equal(action == LeavePolicyActions.Approve, asked.Facts.ContainsKey("isSelfApproval"));
    }

    // ---- a request HR records ----

    [Fact]
    public async Task A_request_recorded_by_HR_asks_the_engine_once_and_Shadow_changes_nothing()
    {
        await using var f = new Fixture();
        f.ShadowWhereTheServerRefuses();

        var id = await f.Create.Handle(new CreateLeaveRequestCommand(f.Dto(Tuesday, Saturday, notes: "  إجازة  ")), default);

        var leave = await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(ApprovalStatus.Pending, leave.Status);
        Assert.Equal(3, leave.DurationDays);
        Assert.Equal("إجازة", leave.Notes);
        var asked = Assert.Single(f.Policy.Requests);
        AssertLeaveQuestion(asked, LeavePolicyActions.Create, id, 3, "Annual");
        // The tenant is the entity's; a request not saved yet has none, which tells the engine to take the tenant of the call.
        Assert.Equal(Guid.Empty, asked.TenantId);
    }

    [Fact]
    public async Task In_Enforce_a_refused_request_is_a_validation_error_and_nothing_is_saved()
    {
        await using var f = new Fixture();
        f.EnforceRefusing();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Create.Handle(new CreateLeaveRequestCommand(f.Dto(Tuesday, Saturday)), default));

        Assert.Equal(PolicyReason, ex.Message);
        Assert.False(await f.Db.LeaveRequests.AnyAsync());
        Assert.False(await f.Db.ServiceTransactions.AnyAsync());
    }

    [Fact]
    public async Task The_local_validation_comes_first_and_the_engine_is_not_asked_for_what_it_refuses()
    {
        await using var f = new Fixture();
        await f.SeedLeaveAsync(ApprovalStatus.Approved, Tuesday, Saturday);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Create.Handle(new CreateLeaveRequestCommand(f.Dto(Tuesday.AddDays(1), Saturday)), default));

        Assert.Contains("توجد إجازة معتمدة متداخلة", ex.Message, StringComparison.Ordinal);
        Assert.Empty(f.Policy.Requests);
    }

    // ---- an edit ----

    [Fact]
    public async Task An_edit_asks_the_engine_with_the_values_it_would_save()
    {
        await using var f = new Fixture();
        var leave = await f.SeedLeaveAsync(ApprovalStatus.Pending, Tuesday, Saturday);
        f.ShadowWhereTheServerRefuses();

        // Sunday 2026-09-06 to Thursday 2026-09-10: five working days, another type.
        var saved = await f.Update.Handle(new UpdateLeaveRequestCommand(leave.Id, f.Dto(new DateOnly(2026, 9, 6), new DateOnly(2026, 9, 10), LeaveType.Sick)), default);

        Assert.True(saved);
        var after = await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == leave.Id);
        Assert.Equal(5, after.DurationDays);
        Assert.Equal(LeaveType.Sick, after.LeaveType);
        var asked = Assert.Single(f.Policy.Requests);
        AssertLeaveQuestion(asked, LeavePolicyActions.Update, leave.Id, 5, "Sick");
        Assert.Equal(after.TenantId, asked.TenantId);
    }

    [Fact]
    public async Task In_Enforce_a_refused_edit_is_a_validation_error_and_the_request_keeps_its_values()
    {
        await using var f = new Fixture();
        var leave = await f.SeedLeaveAsync(ApprovalStatus.Pending, Tuesday, Saturday);
        f.EnforceRefusing();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            f.Update.Handle(new UpdateLeaveRequestCommand(leave.Id, f.Dto(new DateOnly(2026, 9, 6), new DateOnly(2026, 9, 10), LeaveType.Sick)), default));

        Assert.Equal(PolicyReason, ex.Message);
        f.Db.ChangeTracker.Clear();
        var after = await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == leave.Id);
        Assert.Equal(3, after.DurationDays);
        Assert.Equal(LeaveType.Annual, after.LeaveType);
        Assert.Equal(Tuesday, after.StartDate);
    }

    [Fact]
    public async Task An_edit_of_a_decided_request_is_refused_locally_before_the_engine()
    {
        await using var f = new Fixture();
        var leave = await f.SeedLeaveAsync(ApprovalStatus.Rejected, Tuesday, Saturday);

        await Assert.ThrowsAsync<ValidationException>(() => f.Update.Handle(new UpdateLeaveRequestCommand(leave.Id, f.Dto(Tuesday, Saturday)), default));

        Assert.Empty(f.Policy.Requests);
    }

    // ---- the approval ----

    [Fact]
    public async Task An_approval_asks_the_engine_and_says_it_is_not_a_self_approval()
    {
        await using var f = new Fixture();
        var leave = await f.SeedLeaveAsync(ApprovalStatus.Pending, Tuesday, Saturday, completedTransaction: true);
        await f.SeedBalanceAsync();
        f.ShadowWhereTheServerRefuses();

        var (success, error) = await f.Approve.Handle(new ApproveLeaveRequestCommand(leave.Id, f.Checker, Guid.NewGuid()), default);

        Assert.True(success, error);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(ApprovalStatus.Approved, (await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == leave.Id)).Status);
        Assert.Equal(3, (await f.Db.LeaveBalances.AsNoTracking().SingleAsync()).Used);
        var asked = Assert.Single(f.Policy.Requests);
        AssertLeaveQuestion(asked, LeavePolicyActions.Approve, leave.Id, 3, "Annual");
        Assert.Equal("false", asked.Facts["isSelfApproval"]);
        Assert.Equal(leave.TenantId, asked.TenantId);
    }

    [Fact]
    public async Task The_approval_of_ones_own_leave_recorded_by_somebody_else_is_a_self_approval_for_the_engine()
    {
        // The local separation of duties compares the approver with the creator (two user ids). A leave that a colleague, or
        // the self-service portal, recorded for the approver passes it. The engine is told: R-LEAVE-SELF reads the fact.
        await using var f = new Fixture();
        var leave = await f.SeedLeaveAsync(ApprovalStatus.Pending, Tuesday, Saturday, createdBy: f.Maker, completedTransaction: true);
        await f.SeedBalanceAsync();
        f.EnforceRefusing(request => request.Facts.TryGetValue("isSelfApproval", out var self) && self == "true");

        var (success, error) = await f.Approve.Handle(new ApproveLeaveRequestCommand(leave.Id, f.Checker, ApproverEmployeeId: f.Employee), default);

        Assert.False(success);
        Assert.Equal(PolicyReason, error);
        Assert.Equal("true", Assert.Single(f.Policy.Requests).Facts["isSelfApproval"]);
        f.Db.ChangeTracker.Clear();
        // Refused before anything was claimed: still pending, no day debited.
        Assert.Equal(ApprovalStatus.Pending, (await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == leave.Id)).Status);
        Assert.Equal(0, (await f.Db.LeaveBalances.AsNoTracking().SingleAsync()).Used);

        // The same approval by somebody else passes the same policy.
        var (other, otherError) = await f.Approve.Handle(new ApproveLeaveRequestCommand(leave.Id, f.Checker, ApproverEmployeeId: Guid.NewGuid()), default);
        Assert.True(other, otherError);
        Assert.Equal("false", f.Policy.Requests[^1].Facts["isSelfApproval"]);
    }

    [Fact]
    public async Task In_Shadow_a_self_approval_is_recorded_and_the_outcome_is_the_local_one()
    {
        await using var f = new Fixture();
        var leave = await f.SeedLeaveAsync(ApprovalStatus.Pending, Tuesday, Saturday, completedTransaction: true);
        await f.SeedBalanceAsync();
        f.ShadowWhereTheServerRefuses();

        var (success, error) = await f.Approve.Handle(new ApproveLeaveRequestCommand(leave.Id, f.Checker, ApproverEmployeeId: f.Employee), default);

        Assert.True(success, error);
        Assert.Equal("true", Assert.Single(f.Policy.Requests).Facts["isSelfApproval"]);
    }

    [Fact]
    public async Task The_local_separation_of_duties_refuses_the_creator_before_the_engine_is_asked()
    {
        await using var f = new Fixture();
        var leave = await f.SeedLeaveAsync(ApprovalStatus.Pending, Tuesday, Saturday, createdBy: f.Maker, completedTransaction: true);
        await f.SeedBalanceAsync();

        var (success, error) = await f.Approve.Handle(new ApproveLeaveRequestCommand(leave.Id, f.Maker), default);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Empty(f.Policy.Requests);
    }

    // ---- a request from the self-service portal ----

    private static ProcessLeaveRequestedCommand FromPortal(Fixture f, Guid requestId, DateOnly start, DateOnly end, string? notes = "من البوابة") =>
        // The portal sends calendar days: End - Start + 1.
        new(requestId, f.Employee, LeaveType.Annual, start, end, end.DayNumber - start.DayNumber + 1, notes, Guid.NewGuid());

    [Fact]
    public async Task A_self_service_request_passes_the_local_validation_and_the_engine_and_stores_working_days()
    {
        await using var f = new Fixture();
        f.ShadowWhereTheServerRefuses();
        var requestId = Guid.NewGuid();

        await f.SelfService.Handle(FromPortal(f, requestId, Tuesday, Saturday), default);

        var leave = await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
        Assert.Equal(ApprovalStatus.Pending, leave.Status);
        Assert.Equal(3, leave.DurationDays);                // the event said 5 (calendar days)
        Assert.Equal("من البوابة", leave.Notes);
        Assert.NotNull(leave.OrgUnitId);
        var transaction = await f.Db.ServiceTransactions.AsNoTracking().SingleAsync(t => t.Id == leave.ServiceTransactionId);
        Assert.Equal(ServiceTransactionState.Draft, transaction.State);
        var asked = Assert.Single(f.Policy.Requests);
        AssertLeaveQuestion(asked, LeavePolicyActions.Create, requestId, 3, "Annual");
        Assert.Equal(Guid.Empty, asked.TenantId);
    }

    [Fact]
    public async Task In_Enforce_a_refused_self_service_request_is_created_rejected_with_the_reason_and_never_dropped()
    {
        // D-POL-14.
        await using var f = new Fixture();
        f.EnforceRefusing();
        var requestId = Guid.NewGuid();

        await f.SelfService.Handle(FromPortal(f, requestId, Tuesday, Saturday), default);

        var leave = await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
        Assert.Equal(ApprovalStatus.Rejected, leave.Status);
        Assert.Null(leave.ApprovedBy);
        Assert.Equal(ProcessLeaveRequestedHandler.RefusedNotesPrefix + PolicyReason + "\nمن البوابة", leave.Notes);
        Assert.Equal(3, leave.DurationDays);
        // Its transaction is closed, as a rejection by HR closes one that never reached an approval step.
        var transaction = await f.Db.ServiceTransactions.AsNoTracking().Include(t => t.StateHistory).SingleAsync(t => t.Id == leave.ServiceTransactionId);
        Assert.Equal(ServiceTransactionState.Cancelled, transaction.State);
        Assert.Contains(transaction.StateHistory, s => s.Reason == ProcessLeaveRequestedHandler.RefusedTransactionReason);
        Assert.Single(f.Policy.Requests);
    }

    [Fact]
    public async Task A_self_service_request_the_local_validation_refuses_is_created_rejected_in_every_mode()
    {
        await using var f = new Fixture();
        await f.SeedLeaveAsync(ApprovalStatus.Approved, Tuesday, Saturday);
        f.ShadowWhereTheServerRefuses();
        var overlapping = Guid.NewGuid();
        var weekend = Guid.NewGuid();

        await f.SelfService.Handle(FromPortal(f, overlapping, Tuesday.AddDays(1), Tuesday.AddDays(2), notes: null), default);
        // Friday 2026-09-11 and Saturday 2026-09-12: no working day.
        await f.SelfService.Handle(FromPortal(f, weekend, new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 12)), default);

        var first = await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == overlapping);
        Assert.Equal(ApprovalStatus.Rejected, first.Status);
        Assert.Equal(ProcessLeaveRequestedHandler.RefusedNotesPrefix + "توجد إجازة معتمدة متداخلة مع هذه الفترة.", first.Notes);
        var second = await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == weekend);
        Assert.Equal(ApprovalStatus.Rejected, second.Status);
        Assert.StartsWith(ProcessLeaveRequestedHandler.RefusedNotesPrefix + "يجب أن تحتوي فترة الإجازة على يوم عمل واحد على الأقل.", second.Notes, StringComparison.Ordinal);
        Assert.Equal(0, second.DurationDays);
        // The engine is asked only for what the local validation lets through.
        Assert.Empty(f.Policy.Requests);
        Assert.Equal(2, await f.Db.ServiceTransactions.CountAsync(t => t.State == ServiceTransactionState.Cancelled));
    }

    [Fact]
    public async Task A_redelivered_event_creates_no_second_request_and_asks_the_engine_no_second_time()
    {
        await using var f = new Fixture();
        var requestId = Guid.NewGuid();

        await f.SelfService.Handle(FromPortal(f, requestId, Tuesday, Saturday), default);
        await f.SelfService.Handle(FromPortal(f, requestId, Tuesday, Saturday), default);

        Assert.Equal(1, await f.Db.LeaveRequests.CountAsync());
        Assert.Equal(1, await f.Db.ServiceTransactions.CountAsync());
        Assert.Single(f.Policy.Requests);
    }

    [Fact]
    public async Task In_Enforce_an_unreachable_engine_is_not_a_refusal_the_message_fails_and_nothing_is_written()
    {
        // The review of PLAN-49 (finding 3). D-POL-14 is about the policy's refusal; an outage decides nothing.
        await using var f = new Fixture();
        f.Policy.EnforceHandler = request => FakePolicyEnforceClient.FailedClosed(request.Domain);
        var requestId = Guid.NewGuid();

        var failure = await Assert.ThrowsAsync<PolicyEngineUnavailableException>(() => f.SelfService.Handle(FromPortal(f, requestId, Tuesday, Saturday), default));

        Assert.Equal(PolicyDomains.ContractsLeave, failure.Domain);
        Assert.Equal(LeavePolicyActions.Create, failure.Action);
        // The second review of P5: the bus retries only what its policy does not ignore (MassTransitExtensions.ConfigureMessageRetry),
        // and an InvalidOperationException went straight to _error. Pol049EngineOutageRetryTests runs the policy itself.
        Assert.DoesNotContain(
            new[] { typeof(InvalidOperationException), typeof(ArgumentException), typeof(UnauthorizedAccessException) },
            ignored => ignored.IsInstanceOfType(failure));
        Assert.Equal(0, await f.Db.LeaveRequests.CountAsync());
        Assert.Equal(0, await f.Db.ServiceTransactions.CountAsync());

        // The bus delivers the message again; with the engine back the request is decided, once.
        f.Policy.EnforceHandler = null;
        await f.SelfService.Handle(FromPortal(f, requestId, Tuesday, Saturday), default);
        Assert.Equal(ApprovalStatus.Pending, (await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == requestId)).Status);
        Assert.Equal(1, await f.Db.ServiceTransactions.CountAsync());
    }

    [Fact]
    public async Task In_Shadow_an_unreachable_engine_refuses_nothing_and_the_request_is_created()
    {
        await using var f = new Fixture();
        // As the real client answers a failed call in Shadow: allowed, degraded.
        f.Policy.EnforceHandler = _ => new PolicyEnforceResponse
        {
            Allowed = true,
            Degraded = true,
            FailureReason = "http-503",
            EffectiveMode = PolicyEnforcementMode.Shadow,
            Decision = new PolicyDecision { Allowed = true }
        };
        var requestId = Guid.NewGuid();

        await f.SelfService.Handle(FromPortal(f, requestId, Tuesday, Saturday), default);

        Assert.Equal(ApprovalStatus.Pending, (await f.Db.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == requestId)).Status);
    }

    [Fact]
    public async Task The_reference_number_is_taken_before_the_engine_is_asked_so_a_retried_message_asks_it_once()
    {
        // The review of PLAN-49 (finding 5): the number is a call to HR.Core that can fail. Taken after the question, its
        // failure made the bus retry a message whose question was already in the ledger.
        await using var f = new Fixture();
        var noNumber = new ProcessLeaveRequestedHandler(f.Repository, Mock.Of<ICodeGenerationClient>(), f.Policy);
        var requestId = Guid.NewGuid();

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => noNumber.Handle(FromPortal(f, requestId, Tuesday, Saturday), default));

        Assert.Empty(f.Policy.Requests);
        Assert.Equal(0, await f.Db.LeaveRequests.CountAsync());

        await f.SelfService.Handle(FromPortal(f, requestId, Tuesday, Saturday), default);
        Assert.Single(f.Policy.Requests);
    }

    [Fact]
    public void Each_handler_of_the_four_channels_takes_the_policy_client()
    {
        foreach (var handler in new[] { typeof(CreateLeaveRequestHandler), typeof(UpdateLeaveRequestHandler), typeof(ApproveLeaveRequestHandler), typeof(ProcessLeaveRequestedHandler) })
            Assert.Contains(handler.GetConstructors().Single().GetParameters(), p => p.ParameterType == typeof(IPolicyEnforceClient));
    }
}
