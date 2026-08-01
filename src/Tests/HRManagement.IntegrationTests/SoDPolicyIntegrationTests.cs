using System.Reflection;
using System.Security.Claims;
using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using Auth.Directory.Infrastructure.Keycloak;
using Auth.Directory.Web.Controllers;
using HR.Contracts.Application.Features.LeaveRequests;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Web.Controllers;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Controllers;
using HR.Movements.Domain.Entities;
using HR.Movements.Infrastructure.Data;
using HR.Movements.Web.Controllers;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.SeparationOfDuties;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class SoDPolicyIntegrationTests
{
    private static TempDataDictionary TempData(HttpContext ctx) =>
        new(ctx, Mock.Of<ITempDataProvider>());

    private static ClaimsPrincipal User(Guid id, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString("D")),
            new("sub", id.ToString("D"))
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public async Task LeaveApproval_CreatorCannotApprove()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ContractsDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<HR.Contracts.Application.Abstractions.IContractsRepository, HR.Contracts.Infrastructure.Repositories.ContractsRepository>();
        services.AddScoped<HR.Contracts.Application.Abstractions.IEventPublisher>(_ => Mock.Of<HR.Contracts.Application.Abstractions.IEventPublisher>());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ApproveLeaveRequestHandler>());
        await using var root = services.BuildServiceProvider();
        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await db.Database.EnsureCreatedAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var creator = Guid.NewGuid();
        var leave = new LeaveRequest
        {
            EmployeeId = Guid.NewGuid(),
            LeaveType = LeaveType.Annual,
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 6, 5),
            DurationDays = 5,
            Status = ApprovalStatus.Pending,
            CreatedBy = creator
        };
        var tx = new ServiceTransaction
        {
            EmployeeId = leave.EmployeeId,
            TransactionType = ServiceTransactionType.LeaveAdvance
        };
        db.LeaveRequests.Add(leave);
        db.ServiceTransactions.Add(tx);
        await db.SaveChangesAsync();
        leave.ServiceTransactionId = tx.Id;
        await db.SaveChangesAsync();

        var (success, error) = await mediator.Send(new ApproveLeaveRequestCommand(leave.Id, creator));
        Assert.False(success);
        Assert.Contains("منشئ", error!);
    }

    [Fact]
    public async Task TransferApproval_CreatorCannotApprove()
    {
        var options = new DbContextOptionsBuilder<MovementsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new MovementsDbContext(options);
        var creator = Guid.NewGuid();
        var transfer = new TransferRequest
        {
            EmployeeId = Guid.NewGuid(),
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = ApprovalStatus.Pending,
            RequestedBy = creator
        };
        db.TransferRequests.Add(transfer);
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext { User = User(creator, "hr-management-approver") };
        var controller = new TransferRequestsController(db, Mock.Of<IPublishEndpoint>())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = TempData(http)
        };

        var result = await controller.Approve(transfer.Id);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("لا يمكن للمنشئ اعتماد عمله الخاص.", controller.TempData["Error"]);
    }

    [Fact]
    public void TransferApproval_ApproveSignature_HasNoApproverIdParameter()
    {
        var method = typeof(TransferRequestsController).GetMethod(
            nameof(TransferRequestsController.Approve),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.DoesNotContain(method!.GetParameters(), p => p.Name == "approverId");
    }

    [Fact]
    public void LeaveApproval_ApproveSignature_HasNoApproverIdParameter()
    {
        var method = typeof(LeaveRequestsController).GetMethod(
            nameof(LeaveRequestsController.Approve),
            [typeof(Guid)]);
        Assert.NotNull(method);
        Assert.DoesNotContain(method!.GetParameters(), p => p.Name == "approverId");
    }

    [Fact]
    public void PayrollRun_HighAmount_RequiresDualApproval()
    {
        var sod = new SeparationOfDutiesService();
        Assert.True(sod.RequiresDualApproval("PayrollRun", SodMatrixDefaults.DefaultPayrollThreshold + 1));
        var request = ApprovalsController.CreateApprovalRequest(
            "PayrollRun",
            Guid.NewGuid(),
            "large",
            Guid.NewGuid(),
            "proc",
            financialAmount: SodMatrixDefaults.DefaultPayrollThreshold + 1);
        Assert.True(request.Steps.Count >= 2);
    }

    [Fact]
    public async Task RoleChange_RequiresDualApproval_ForPrivilegedRole()
    {
        var options = new DbContextOptionsBuilder<AuthDirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AuthDirectoryDbContext(options);
        var assignor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var privileged = new Role { Name = "hr-admin", Description = "admin" };
        db.Roles.Add(privileged);
        db.AuthUsers.Add(new AuthUser
        {
            Id = target,
            Username = "target",
            Email = "t@test.local",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext { User = User(assignor, "hr-admin") };
        var controller = new AuthUsersController(db, Mock.Of<IKeycloakUserProvisioningService>())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = TempData(http)
        };

        var result = await controller.Edit(target, true, [privileged.Id], secondApproverId: null);
        Assert.IsType<ViewResult>(result);
        Assert.Contains("ثانٍ", controller.TempData["Error"]?.ToString() ?? "");
    }

    [Fact]
    public async Task SelfRoleGrant_Rejected()
    {
        var options = new DbContextOptionsBuilder<AuthDirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AuthDirectoryDbContext(options);
        var userId = Guid.NewGuid();
        var role = new Role { Name = "hr-core" };
        db.Roles.Add(role);
        db.AuthUsers.Add(new AuthUser
        {
            Id = userId,
            Username = "self",
            Email = "s@test.local",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext { User = User(userId, "hr-admin") };
        var controller = new AuthUsersController(db, Mock.Of<IKeycloakUserProvisioningService>())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = TempData(http)
        };

        var result = await controller.Edit(userId, true, [role.Id], null);
        Assert.IsType<ViewResult>(result);
        Assert.Contains("نفسه", controller.TempData["Error"]?.ToString() ?? "");
    }

    [Fact]
    public async Task Delegation_ActiveDelegateCanBeResolved()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new HRCoreDbContext(options);
        var principal = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.ApprovalDelegations.Add(new ApprovalDelegation
        {
            PrincipalId = principal,
            DelegateId = delegateId,
            ActiveFrom = now.AddHours(-1),
            ActiveUntil = now.AddDays(3),
            Reason = "سفر",
            EntityTypeScope = "LeaveRequest"
        });
        await db.SaveChangesAsync();

        var sod = new SeparationOfDutiesService(delegations: await db.ApprovalDelegations.ToListAsync());
        Assert.Equal(delegateId, sod.ResolveDelegate(principal, "LeaveRequest", now));
    }

    [Fact]
    public void Delegation_ExpiredDelegateCannotApprove()
    {
        var principal = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var sod = new SeparationOfDutiesService(delegations:
        [
            new ApprovalDelegation
            {
                PrincipalId = principal,
                DelegateId = delegateId,
                ActiveFrom = now.AddDays(-10),
                ActiveUntil = now.AddDays(-1),
                Reason = "منتهٍ"
            }
        ]);

        Assert.Null(sod.ResolveDelegate(principal, "LeaveRequest", now));
        Assert.False(sod.IsActingAsDelegate(delegateId, principal, "LeaveRequest", now));
    }

    [Fact]
    public void ApprovalWorkflow_BlocksSelfApprove_EndToEnd()
    {
        var creator = Guid.NewGuid();
        var request = ApprovalsController.CreateApprovalRequest(
            "ApprovalRequest", Guid.NewGuid(), "x", creator, "c");
        Assert.False(ApprovalWorkflowService.TryApproveStep(request, creator, "c", null, out var err));
        Assert.Contains("منشئ", err!);
    }
}
