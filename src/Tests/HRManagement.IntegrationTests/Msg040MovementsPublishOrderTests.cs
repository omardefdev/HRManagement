using System.Security.Claims;
using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Infrastructure.Data;
using HR.Movements.Web.Controllers;
using HRManagement.SharedKernel.Legal;
using HRManagement.UnitTests.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-40 (§2, class A): promotion and delegation execution stage their event before a single save. Here and not in the
/// unit tests because they need the HR.Movements.Web controllers (OutboxOrderProbe is linked from the unit test project).
/// </summary>
public sealed class Msg040MovementsPublishOrderTests
{
    private static readonly Guid Tenant = Guid.Parse("a0000001-0000-4000-8000-000000000001");

    private static T WithUser<T>(T controller, Guid userId) where T : Controller
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static void AddGateSteps(MovementsDbContext db, MovementKind kind, Guid correlationId)
    {
        foreach (var step in new[] { MovementChecklistStep.RequestRecorded, MovementChecklistStep.Approved })
        {
            db.MovementChecklists.Add(new MovementChecklist
            {
                MovementKind = kind,
                CorrelationId = correlationId,
                Step = step,
                IsRequired = true,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow,
                EvidenceRef = $"test:{step}",
                TenantId = Tenant
            });
        }
    }

    [Fact]
    public async Task Promotion_execute_publishes_before_its_single_save()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new MovementsDbContext(probe.InMemoryOptions<MovementsDbContext>());
        var promotion = new PromotionRequest
        {
            EmployeeId = Guid.NewGuid(), FromGrade = 5, ToGrade = 4, FromStep = 3, ToStep = 1,
            TransitionDecisionRef = "DEC-40", EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = PromotionRequestStatus.Approved, TenantId = Tenant
        };
        db.PromotionRequests.Add(promotion);
        AddGateSteps(db, MovementKind.Promotion, promotion.Id);
        await db.SaveChangesAsync();
        probe.Clear();

        var controller = WithUser(new PromotionRequestsController(db, probe.Publish, Mock.Of<IHrCoreActiveRegimeClient>()), Guid.NewGuid());
        var result = await controller.Execute(promotion.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(["publish:IPromotionExecuted", "save"], probe.Log);
        Assert.Equal(PromotionRequestStatus.Executed, (await db.PromotionRequests.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Delegation_execute_publishes_before_its_single_save()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new MovementsDbContext(probe.InMemoryOptions<MovementsDbContext>());
        var delegation = new DelegationRequest
        {
            EmployeeId = Guid.NewGuid(), HostOrg = "Host",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = DelegationRequestStatus.Approved, TenantId = Tenant
        };
        db.DelegationRequests.Add(delegation);
        AddGateSteps(db, MovementKind.Delegation, delegation.Id);
        await db.SaveChangesAsync();
        probe.Clear();

        var controller = WithUser(new DelegationRequestsController(db, probe.Publish), Guid.NewGuid());
        var result = await controller.Execute(delegation.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(["publish:IDelegationExecuted", "save"], probe.Log);
        Assert.Equal(DelegationRequestStatus.Active, (await db.DelegationRequests.AsNoTracking().SingleAsync()).Status);
    }
}

/// <summary>
/// PLAN-40: a consumer finds a transaction without its history and moves it, as TransactionArchivedConsumer does. The new
/// history row must be inserted; with a generated key it counted as existing and the save failed on every delivery.
/// </summary>
public sealed class Msg040ServiceTransactionHistoryTests
{
    [Fact]
    public async Task Moving_a_transaction_found_without_its_history_saves_the_new_row()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HR.Contracts.Infrastructure.Data.ContractsDbContext>().UseSqlite(connection).Options;
        var transaction = TestDataFactory.CreateLeaveTransaction(Guid.NewGuid());
        HR.Contracts.Domain.Services.ServiceTransactionWorkflowHelper.AdvanceToCompleted(transaction, Guid.NewGuid(), "seed");
        await using (var seed = new HR.Contracts.Infrastructure.Data.ContractsDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.ServiceTransactions.Add(transaction);
            await seed.SaveChangesAsync();
        }

        await using var db = new HR.Contracts.Infrastructure.Data.ContractsDbContext(options);
        var found = await db.ServiceTransactions.FindAsync(transaction.Id);
        var before = await db.ServiceTransactionStateHistories.CountAsync();
        found!.MoveTo(HR.Contracts.Domain.Enums.ServiceTransactionState.Archived, null, "Archived: ref");
        await db.SaveChangesAsync();

        Assert.Equal(before + 1, await db.ServiceTransactionStateHistories.CountAsync());
    }
}

/// <summary>
/// PLAN-40 (review): a move refused inside the unit of work rolls back the fields saved before it, as the two org-unit
/// controllers do it, so no state stays behind without its event.
/// </summary>
public sealed class Msg040OrgUnitMoveRollbackTests
{
    [Fact]
    public async Task A_refused_move_leaves_the_unit_as_it_was_and_a_valid_one_joins_the_transaction()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HR.Core.Infrastructure.Data.HRCoreDbContext>().UseSqlite(connection).Options;
        await using var db = new HR.Core.Infrastructure.Data.HRCoreDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var parent = new HR.Core.Domain.Entities.OrgUnit { Code = "P", NameAr = "أصل", NameEn = "P" };
        parent.MaterializedPath = $"/{parent.Id:D}/";
        var child = new HR.Core.Domain.Entities.OrgUnit { Code = "C", NameAr = "فرع", NameEn = "C", ParentId = parent.Id };
        child.MaterializedPath = $"{parent.MaterializedPath}{child.Id:D}/";
        var other = new HR.Core.Domain.Entities.OrgUnit { Code = "O", NameAr = "آخر", NameEn = "O" };
        other.MaterializedPath = $"/{other.Id:D}/";
        db.OrgUnits.AddRange(parent, child, other);
        await db.SaveChangesAsync();
        var paths = new HR.Core.Infrastructure.OrgUnitPathService(db);

        async Task MoveAsync(Guid unit, Guid target, string name)
        {
            await HRManagement.ServiceDefaults.Messaging.OutboxUnitOfWork.ExecuteAsync(db, async ct =>
            {
                (await db.OrgUnits.SingleAsync(u => u.Id == unit, ct)).NameAr = name;
                await db.SaveChangesAsync(ct);
                var (moved, error, _) = await paths.MoveSubtreeAsync(unit, target, ct);
                if (!moved)
                    throw new HR.Core.Infrastructure.OrgUnitMoveRefusedException(error);
            });
        }

        await Assert.ThrowsAsync<HR.Core.Infrastructure.OrgUnitMoveRefusedException>(() => MoveAsync(parent.Id, child.Id, "اسم مرفوض"));
        db.ChangeTracker.Clear();
        Assert.Equal("أصل", (await db.OrgUnits.AsNoTracking().SingleAsync(u => u.Id == parent.Id)).NameAr);

        await MoveAsync(child.Id, other.Id, "فرع منقول");
        db.ChangeTracker.Clear();
        var moved = await db.OrgUnits.AsNoTracking().SingleAsync(u => u.Id == child.Id);
        Assert.Equal(other.Id, moved.ParentId);
        Assert.Equal("فرع منقول", moved.NameAr);
    }
}
