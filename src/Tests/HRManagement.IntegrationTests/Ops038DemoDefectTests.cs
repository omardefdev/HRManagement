using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.AdvanceRequests;
using HR.Contracts.Application.Features.LeaveRequests;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.TestSupport.CodeGeneration;
using IDCard.Domain.Entities;
using IDCard.Infrastructure.Data;
using IDCard.Web.Controllers;
using IDCard.Web.Models;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-38 (P6), the two demo-breaking defects of PLAN-37 (§11), on PostgreSQL:
/// <list type="number">
/// <item>Leave approval claims the row with ExecuteUpdate; saving the tracked entity afterwards used the old xmin and
/// failed every time (DbUpdateConcurrencyException, HTTP 500). Now one approval succeeds, and a second approval racing it
/// (it read the request while it was still Pending) loses cleanly with "not pending" and no double debit. Advance
/// approval has no separate claim: it succeeds, and the racing loser gets the xmin conflict that
/// ConcurrencyConflictMiddleware answers with HTTP 409, without a second installment plan.</item>
/// <item>IDCard <c>IssuedCards/Create</c> bound the entity, whose non-nullable navigation MVC treats as required, so it
/// never succeeded; it binds <see cref="IssuedCardFormModel"/> now.</item>
/// </list>
/// </summary>
[Collection("PostgreSql")]
public sealed class Ops038DemoDefectTests(PostgreSqlFixture fixture)
{
    private const string Skip = "Set RUN_TESTCONTAINERS=true and start Docker to run this test.";
    private static readonly Guid Approver = Guid.NewGuid();

    private static ContractsDbContext Contracts(string cs) =>
        new(new DbContextOptionsBuilder<ContractsDbContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(ContractsDbContext).Assembly.GetName().Name))
            .Options);

    /// <summary>Holds a handler right after it read the request (still Pending) until the competing approval finished.</summary>
    private sealed class GatedRepository(IContractsRepository inner, Task gate) : IContractsRepository
    {
        public IQueryable<LeaveRequest> LeaveRequests => inner.LeaveRequests;
        public IQueryable<AdvanceRequest> AdvanceRequests => inner.AdvanceRequests;
        public IQueryable<AdministrativeDecision> AdministrativeDecisions => inner.AdministrativeDecisions;
        public IQueryable<CommencementRecord> CommencementRecords => inner.CommencementRecords;
        public IQueryable<ProbationRecord> ProbationRecords => inner.ProbationRecords;
        public IQueryable<SeparationRecord> SeparationRecords => inner.SeparationRecords;
        public IQueryable<ContractType> ContractTypes => inner.ContractTypes;
        public IQueryable<ContractTypeRegimeMap> ContractTypeRegimeMaps => inner.ContractTypeRegimeMaps;
        public IQueryable<Contract> Contracts => inner.Contracts;
        public IQueryable<ServiceTransaction> ServiceTransactions => inner.ServiceTransactions;
        public IQueryable<LeaveBalance> LeaveBalances => inner.LeaveBalances;
        public IQueryable<LeavePolicy> LeavePolicies => inner.LeavePolicies;
        public IQueryable<AdvanceInstallmentPlan> AdvanceInstallmentPlans => inner.AdvanceInstallmentPlans;
        public IQueryable<SeparationChecklist> SeparationChecklists => inner.SeparationChecklists;
        public IQueryable<HireChecklist> HireChecklists => inner.HireChecklists;

        public async Task<LeaveRequest?> GetLeaveRequestAsync(Guid id, CancellationToken ct)
        {
            var entity = await inner.GetLeaveRequestAsync(id, ct);
            await gate;
            return entity;
        }

        public async Task<AdvanceRequest?> GetAdvanceRequestAsync(Guid id, CancellationToken ct)
        {
            var entity = await inner.GetAdvanceRequestAsync(id, ct);
            await gate;
            return entity;
        }

        public Task<AdministrativeDecision?> GetAdministrativeDecisionAsync(Guid id, CancellationToken ct) => inner.GetAdministrativeDecisionAsync(id, ct);
        public Task<CommencementRecord?> GetCommencementRecordAsync(Guid id, CancellationToken ct) => inner.GetCommencementRecordAsync(id, ct);
        public Task<SeparationRecord?> GetSeparationRecordAsync(Guid id, CancellationToken ct) => inner.GetSeparationRecordAsync(id, ct);
        public Task<ServiceTransaction?> GetServiceTransactionAsync(Guid id, CancellationToken ct) => inner.GetServiceTransactionAsync(id, ct);
        public Task<Contract?> GetContractAsync(Guid id, CancellationToken ct) => inner.GetContractAsync(id, ct);
        public Task AddAsync<T>(T entity, CancellationToken ct) where T : class => inner.AddAsync(entity, ct);
        public void Remove<T>(T entity) where T : class => inner.Remove(entity);
        public Task<int> SaveChangesAsync(CancellationToken ct) => inner.SaveChangesAsync(ct);
        public Task ReloadAsync<T>(T entity, CancellationToken ct) where T : class => inner.ReloadAsync(entity, ct);
    }

    /// <summary>Names the stale entities of a concurrency failure (types and states only, no values).</summary>
    private static async Task<T> Describe<T>(Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException(
                "stale: " + string.Join(", ", ex.Entries.Select(e => $"{e.Metadata.ClrType.Name}/{e.State}")), ex);
        }
    }

    private async Task<(string Cs, Guid LeaveId, Guid AdvanceId, Guid BalanceId)> SeedAsync()
    {
        var cs = await fixture.CreateDatabaseAsync(TestDatabaseNames.Unique("ops038_p6_"));
        await using var db = Contracts(cs);
        await db.Database.MigrateAsync();
        var employee = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var leaveTx = new ServiceTransaction { EmployeeId = employee, TransactionType = ServiceTransactionType.LeaveAdvance };
        var advanceTx = new ServiceTransaction { EmployeeId = employee, TransactionType = ServiceTransactionType.LeaveAdvance };
        db.ServiceTransactions.AddRange(leaveTx, advanceTx);
        await db.SaveChangesAsync();

        var leave = new LeaveRequest
        {
            EmployeeId = employee, LeaveType = LeaveType.Annual, StartDate = start, EndDate = start.AddDays(2),
            DurationDays = 3, Status = ApprovalStatus.Pending, ServiceTransactionId = leaveTx.Id,
        };
        var balance = new LeaveBalance { EmployeeId = employee, LeaveType = LeaveType.Annual, Year = start.Year, TotalEntitlement = 30, Used = 0 };
        var advance = new AdvanceRequest
        {
            EmployeeId = employee, Amount = 1000m, Reason = "PLAN-38 P6", Status = ApprovalStatus.Pending, ServiceTransactionId = advanceTx.Id,
        };
        db.LeaveRequests.Add(leave);
        db.LeaveBalances.Add(balance);
        db.AdvanceRequests.Add(advance);
        await db.SaveChangesAsync();
        return (cs, leave.Id, advance.Id, balance.Id);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Leave_approval_succeeds_on_postgresql_and_a_racing_second_approval_loses_cleanly()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var (cs, leaveId, _, balanceId) = await SeedAsync();

        await using var winnerDb = Contracts(cs);
        await using var loserDb = Contracts(cs);
        var gate = new TaskCompletionSource();
        var loser = new ApproveLeaveRequestHandler(new GatedRepository(new ContractsRepository(loserDb), gate.Task), Mock.Of<IEventPublisher>())
            .Handle(new ApproveLeaveRequestCommand(leaveId, Approver), CancellationToken.None);

        // The loser has read the request (Pending) and waits; the winner approves completely.
        var winner = await Describe(() => new ApproveLeaveRequestHandler(new ContractsRepository(winnerDb), Mock.Of<IEventPublisher>())
            .Handle(new ApproveLeaveRequestCommand(leaveId, Approver), CancellationToken.None));
        Assert.True(winner.Success, winner.Error);

        gate.SetResult();
        var lost = await loser; // no exception: the claim matched no Pending row
        Assert.False(lost.Success);
        Assert.Equal("الطلب ليس قيد الانتظار", lost.Error);

        await using var verify = Contracts(cs);
        var leave = await verify.LeaveRequests.AsNoTracking().SingleAsync(l => l.Id == leaveId);
        Assert.Equal(ApprovalStatus.Approved, leave.Status);
        Assert.Equal(Approver, leave.ApprovedBy);
        Assert.Equal(3, await verify.LeaveBalances.Where(b => b.Id == balanceId).Select(b => b.Used).SingleAsync());
        Assert.Equal(ServiceTransactionState.Completed,
            await verify.ServiceTransactions.Where(t => t.Id == leave.ServiceTransactionId).Select(t => t.State).SingleAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Leave_approval_refuses_a_request_edited_after_it_was_read_and_debits_nothing()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var (cs, leaveId, _, balanceId) = await SeedAsync();

        await using var approverDb = Contracts(cs);
        var gate = new TaskCompletionSource();
        var approval = new ApproveLeaveRequestHandler(new GatedRepository(new ContractsRepository(approverDb), gate.Task), Mock.Of<IEventPublisher>())
            .Handle(new ApproveLeaveRequestCommand(leaveId, Approver), CancellationToken.None);

        // The approver validated a 3-day leave; an edit to 20 days commits before its claim (review finding F1).
        await using (var editorDb = Contracts(cs))
        {
            var edited = await editorDb.LeaveRequests.Where(l => l.Id == leaveId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.DurationDays, 20)
                    .SetProperty(l => l.EndDate, l => l.StartDate.AddDays(19)));
            Assert.Equal(1, edited);
        }

        gate.SetResult();
        var result = await approval;
        Assert.False(result.Success);
        Assert.Equal(ApproveLeaveRequestHandler.LeaveRequestChangedError, result.Error);

        await using var verify = Contracts(cs);
        var leave = await verify.LeaveRequests.AsNoTracking().SingleAsync(l => l.Id == leaveId);
        Assert.Equal(ApprovalStatus.Pending, leave.Status);
        Assert.Null(leave.ApprovedBy);
        Assert.Equal(20, leave.DurationDays);
        Assert.Equal(0, await verify.LeaveBalances.Where(b => b.Id == balanceId).Select(b => b.Used).SingleAsync());

        // Read again, the edited request is validated and approved as edited.
        await using var againDb = Contracts(cs);
        var again = await new ApproveLeaveRequestHandler(new ContractsRepository(againDb), Mock.Of<IEventPublisher>())
            .Handle(new ApproveLeaveRequestCommand(leaveId, Approver), CancellationToken.None);
        Assert.True(again.Success, again.Error);
        Assert.Equal(20, await verify.LeaveBalances.Where(b => b.Id == balanceId).Select(b => b.Used).SingleAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Leave_cancellation_after_approval_succeeds_on_postgresql()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var (cs, leaveId, _, balanceId) = await SeedAsync();
        await using (var db = Contracts(cs))
        {
            var approved = await new ApproveLeaveRequestHandler(new ContractsRepository(db), Mock.Of<IEventPublisher>())
                .Handle(new ApproveLeaveRequestCommand(leaveId, Approver), CancellationToken.None);
            Assert.True(approved.Success, approved.Error);
        }

        await using (var db = Contracts(cs))
        {
            var cancelled = await new CancelLeaveRequestHandler(new ContractsRepository(db), Mock.Of<IEventPublisher>())
                .Handle(new CancelLeaveRequestCommand(leaveId, Approver), CancellationToken.None);
            Assert.True(cancelled.Success, cancelled.Error);
        }

        await using var verify = Contracts(cs);
        Assert.Equal(ApprovalStatus.Cancelled, await verify.LeaveRequests.Where(l => l.Id == leaveId).Select(l => l.Status).SingleAsync());
        Assert.Equal(0, await verify.LeaveBalances.Where(b => b.Id == balanceId).Select(b => b.Used).SingleAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Advance_approval_succeeds_on_postgresql_and_a_racing_second_approval_gets_the_conflict()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var (cs, _, advanceId, _) = await SeedAsync();

        await using var winnerDb = Contracts(cs);
        await using var loserDb = Contracts(cs);
        var gate = new TaskCompletionSource();
        var loser = new ApproveAdvanceRequestHandler(new GatedRepository(new ContractsRepository(loserDb), gate.Task), Mock.Of<IEventPublisher>())
            .Handle(new ApproveAdvanceRequestCommand(advanceId, Approver), CancellationToken.None);

        var winner = await new ApproveAdvanceRequestHandler(new ContractsRepository(winnerDb), Mock.Of<IEventPublisher>())
            .Handle(new ApproveAdvanceRequestCommand(advanceId, Approver), CancellationToken.None);
        Assert.True(winner.Success, winner.Error);

        gate.SetResult();
        // The stale write is refused by the xmin token; ConcurrencyConflictMiddleware turns it into HTTP 409, not 500.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => loser);

        await using var verify = Contracts(cs);
        Assert.Equal(ApprovalStatus.Approved, await verify.AdvanceRequests.Where(a => a.Id == advanceId).Select(a => a.Status).SingleAsync());
        Assert.Equal(1, await verify.AdvanceInstallmentPlans.CountAsync(p => p.AdvanceRequestId == advanceId));
    }

    // ------------------------------------------------------------------------------------------- IDCard

    private static IssuedCardsController CardsController(IDCardDbContext db, FakeCodeGenerationClient codes, out ServiceProvider services)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddControllersWithViews();
        services = collection.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        return new IssuedCardsController(db, Mock.Of<IPublishEndpoint>(), Mock.Of<IFileReferenceClient>(), codes)
        {
            ControllerContext = new ControllerContext { HttpContext = http, RouteData = new Microsoft.AspNetCore.Routing.RouteData() },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>()),
            // MVC's own validator and metadata (implicit [Required] for non-nullable reference types, as in the app).
            ObjectValidator = services.GetRequiredService<IObjectModelValidator>(),
            MetadataProvider = services.GetRequiredService<IModelMetadataProvider>(),
        };
    }

    private async Task<(string Cs, Guid DesignId)> SeedCardsAsync()
    {
        var cs = await fixture.CreateDatabaseAsync(TestDatabaseNames.Unique("ops038_idcard_"));
        var options = new DbContextOptionsBuilder<IDCardDbContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(IDCardDbContext).Assembly.GetName().Name)).Options;
        await using var db = new IDCardDbContext(options);
        await db.Database.MigrateAsync();
        var design = new CardDesign { Name = "PLAN-38 design" };
        db.CardDesigns.Add(design);
        await db.SaveChangesAsync();
        return (cs, design.Id);
    }

    private static IDCardDbContext Cards(string cs) =>
        new(new DbContextOptionsBuilder<IDCardDbContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(IDCardDbContext).Assembly.GetName().Name)).Options);

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Id_card_create_binds_the_form_model_and_issues_a_card_with_a_generated_serial()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var (cs, designId) = await SeedCardsAsync();
        await using var db = Cards(cs);
        var codes = new FakeCodeGenerationClient();
        var controller = CardsController(db, codes, out var services);
        await using var _ = services;

        // The defect: the entity can never be valid as a form (CardDesign navigation is implicitly required).
        Assert.False(controller.TryValidateModel(new IssuedCard { EmployeeId = Guid.NewGuid(), CardDesignId = designId, SerialNumber = "x" }));
        Assert.Contains(controller.ModelState, e => e.Key.Contains("CardDesign", StringComparison.Ordinal) && e.Value!.Errors.Count > 0);

        controller.ModelState.Clear();
        var employee = Guid.NewGuid();
        var model = new IssuedCardFormModel { EmployeeId = employee, CardDesignId = designId };
        Assert.True(controller.TryValidateModel(model));
        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        await using var verify = Cards(cs);
        var card = await verify.IssuedCards.AsNoTracking().SingleAsync(c => c.EmployeeId == employee);
        Assert.Equal(designId, card.CardDesignId);
        Assert.StartsWith("TEST-", card.SerialNumber, StringComparison.Ordinal);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Id_card_create_refuses_a_missing_employee_or_an_unknown_design_and_saves_nothing()
    {
        Xunit.Skip.IfNot(RealInfra.IsEnabled, Skip);
        var (cs, _) = await SeedCardsAsync();
        await using var db = Cards(cs);
        var controller = CardsController(db, new FakeCodeGenerationClient(), out var services);
        await using var _ = services;

        var noEmployee = new IssuedCardFormModel { CardDesignId = Guid.NewGuid() };
        Assert.False(controller.TryValidateModel(noEmployee));
        Assert.IsType<ViewResult>(await controller.Create(noEmployee));
        Assert.True(controller.ModelState[nameof(IssuedCardFormModel.EmployeeId)]!.Errors.Count > 0);

        controller.ModelState.Clear();
        var unknownDesign = new IssuedCardFormModel { EmployeeId = Guid.NewGuid(), CardDesignId = Guid.NewGuid() };
        Assert.True(controller.TryValidateModel(unknownDesign));
        Assert.IsType<ViewResult>(await controller.Create(unknownDesign));
        Assert.True(controller.ModelState[nameof(IssuedCardFormModel.CardDesignId)]!.Errors.Count > 0);

        await using var verify = Cards(cs);
        Assert.Equal(0, await verify.IssuedCards.CountAsync());
    }
}
