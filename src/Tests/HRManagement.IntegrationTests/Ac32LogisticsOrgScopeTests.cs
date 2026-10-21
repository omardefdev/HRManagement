using System.Security.Claims;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Helpers;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class Ac32LogisticsOrgScopeTests
{
    private sealed class ScopeProbeController : Controller;

    private static ClaimsPrincipal BranchManager(Guid branchOrgUnitId) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager),
            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, branchOrgUnitId.ToString("D"))
        ], "test"));

    private static ClaimsPrincipal HrCenter() =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)
        ], "test"));

    [Fact]
    public async Task Sibling_branch_manager_forbidden_on_br1_custody_asset()
    {
        await using var db = await CreateSeededDbAsync();
        var controller = CreateController(BranchManager(AcceptanceSeedIds.Br2OrgUnitId));

        var denied = await controller.ForbidIfAssetOutOfScopeAsync(db, AcceptanceSeedIds.AcceptanceAssetBr1Id);
        Assert.IsType<ForbidResult>(denied);
    }

    [Fact]
    public async Task Owning_branch_manager_allowed_on_br1_custody_asset()
    {
        await using var db = await CreateSeededDbAsync();
        var controller = CreateController(BranchManager(AcceptanceSeedIds.Br1OrgUnitId));

        var denied = await controller.ForbidIfAssetOutOfScopeAsync(db, AcceptanceSeedIds.AcceptanceAssetBr1Id);
        Assert.Null(denied);
    }

    [Fact]
    public async Task Hr_center_unrestricted_allowed_on_br1_custody_asset()
    {
        await using var db = await CreateSeededDbAsync();
        var controller = CreateController(HrCenter());

        var denied = await controller.ForbidIfAssetOutOfScopeAsync(db, AcceptanceSeedIds.AcceptanceAssetBr1Id);
        Assert.Null(denied);
    }

    [Fact]
    public async Task Assign_to_sibling_employee_forbidden_for_br1_manager()
    {
        await using var db = await CreateSeededDbAsync();
        var controller = CreateController(BranchManager(AcceptanceSeedIds.Br1OrgUnitId));

        var denied = await controller.ForbidIfEmployeeOutOfScopeForCustodyAsync(db, AcceptanceSeedIds.EmpBr2Id);
        Assert.IsType<ForbidResult>(denied);
    }

    [Fact]
    public async Task EmployeeCreatedConsumer_upserts_snapshot()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AdminLogisticsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var employeeId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var consumer = new EmployeeCreatedConsumer(db, Mock.Of<ILogger<EmployeeCreatedConsumer>>());
        var message = Mock.Of<IEmployeeCreated>(m =>
            m.EmployeeId == employeeId
            && m.PrimaryOrgUnitId == orgUnitId
            && m.TenantId == AcceptanceSeedIds.AgencyAId);
        var context = Mock.Of<ConsumeContext<IEmployeeCreated>>(c =>
            c.Message == message && c.CancellationToken == CancellationToken.None);

        await consumer.Consume(context);

        var snap = await db.EmployeeOrgUnitSnapshots.FindAsync(employeeId);
        Assert.NotNull(snap);
        Assert.Equal(orgUnitId, snap!.PrimaryOrgUnitId);
    }

    private static ScopeProbeController CreateController(ClaimsPrincipal user)
    {
        var controller = new ScopeProbeController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
        return controller;
    }

    private static async Task<AdminLogisticsDbContext> CreateSeededDbAsync()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await AcceptanceLogisticsSeeder.SeedAsync(db);
        return db;
    }
}
