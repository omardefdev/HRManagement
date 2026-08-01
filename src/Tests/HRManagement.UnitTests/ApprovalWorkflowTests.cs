using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Controllers;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;

namespace HRManagement.UnitTests;

public class ApprovalWorkflowTests
{
    private HRCoreDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new HRCoreDbContext(options);
    }

    private ClaimsPrincipal CreateUser(string role = "hr-core", string userId = "00000000-0000-0000-0000-000000000001")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("preferred_username", "testuser"),
            new(ClaimTypes.Role, role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private ApprovalsController CreateController(
        HRCoreDbContext ctx,
        string role = "hr-core",
        string userId = "00000000-0000-0000-0000-000000000001")
    {
        var httpContext = new DefaultHttpContext { User = CreateUser(role, userId) };
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return new ApprovalsController(ctx)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = tempData
        };
    }

    [Fact]
    public void CreateApprovalRequest_SingleLevel_CreatesOneStep()
    {
        var request = ApprovalsController.CreateApprovalRequest(
            "Employee", Guid.NewGuid(), "طلب تعيين", null, "أحمد");

        Assert.Equal(ApprovalStatus.Pending, request.Status);
        Assert.Single(request.Steps);
        Assert.Equal(1, request.Steps.First().Level);
        Assert.Equal("hr-core", request.Steps.First().RoleRequired);
    }

    [Fact]
    public void CreateApprovalRequest_MultiLevel_CreatesOrderedSteps()
    {
        var request = ApprovalsController.CreateApprovalRequest(
            "Evaluation", Guid.NewGuid(), "طلب تقييم", null, "سامي",
            requiredRoles: ["hr-core", "hr-admin"]);

        Assert.Equal(2, request.Steps.Count);
        var steps = request.Steps.OrderBy(s => s.Level).ToList();
        Assert.Equal(1, steps[0].Level);
        Assert.Equal("hr-core", steps[0].RoleRequired);
        Assert.Equal(2, steps[1].Level);
        Assert.Equal("hr-admin", steps[1].RoleRequired);
    }

    [Fact]
    public async Task Approve_PendingRequest_SingleLevel_ApprovesAndCompletes()
    {
        using var ctx = CreateContext();
        var request = ApprovalsController.CreateApprovalRequest(
            "Employee", Guid.NewGuid(), "test", null, "user");
        ctx.ApprovalRequests.Add(request);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx);

        var result = await controller.Approve(request.Id, "looks good");

        var updated = await ctx.ApprovalRequests.Include(r => r.Steps).FirstAsync(r => r.Id == request.Id);
        Assert.Equal(ApprovalStatus.Approved, updated.Status);
        Assert.NotNull(updated.ReviewedAt);
        Assert.Equal(ApprovalStatus.Approved, updated.Steps.First().Status);
    }

    [Fact]
    public async Task Approve_MultiLevel_FirstApproval_AdvancesLevel()
    {
        using var ctx = CreateContext();
        var request = ApprovalsController.CreateApprovalRequest(
            "Employee", Guid.NewGuid(), "test", null, "user",
            requiredRoles: ["hr-core", "hr-admin"]);
        ctx.ApprovalRequests.Add(request);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx);

        await controller.Approve(request.Id, "level 1 ok");

        var updated = await ctx.ApprovalRequests.Include(r => r.Steps).FirstAsync(r => r.Id == request.Id);
        Assert.Equal(ApprovalStatus.Pending, updated.Status);
        Assert.Equal(2, updated.CurrentLevel);

        var step1 = updated.Steps.First(s => s.Level == 1);
        Assert.Equal(ApprovalStatus.Approved, step1.Status);
    }

    [Fact]
    public async Task Approve_MultiLevel_AllLevels_FullyApproves()
    {
        using var ctx = CreateContext();
        var request = ApprovalsController.CreateApprovalRequest(
            "Employee", Guid.NewGuid(), "test", null, "user",
            requiredRoles: ["hr-core", "hr-admin"]);
        ctx.ApprovalRequests.Add(request);
        await ctx.SaveChangesAsync();

        var first = CreateController(ctx, "hr-core", "00000000-0000-0000-0000-000000000001");
        await first.Approve(request.Id, "level 1 ok");

        var second = CreateController(ctx, "hr-admin", "00000000-0000-0000-0000-000000000002");
        await second.Approve(request.Id, "level 2 ok");

        var updated = await ctx.ApprovalRequests.Include(r => r.Steps).FirstAsync(r => r.Id == request.Id);
        Assert.Equal(ApprovalStatus.Approved, updated.Status);
        Assert.All(updated.Steps, s => Assert.Equal(ApprovalStatus.Approved, s.Status));
    }

    [Fact]
    public async Task Reject_PendingRequest_SetsRejected()
    {
        using var ctx = CreateContext();
        var request = ApprovalsController.CreateApprovalRequest(
            "Employee", Guid.NewGuid(), "test", null, "user");
        ctx.ApprovalRequests.Add(request);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx);

        await controller.Reject(request.Id, "not acceptable");

        var updated = await ctx.ApprovalRequests.Include(r => r.Steps).FirstAsync(r => r.Id == request.Id);
        Assert.Equal(ApprovalStatus.Rejected, updated.Status);
        Assert.Equal("not acceptable", updated.ReviewComments);
    }

    [Fact]
    public async Task Cancel_PendingRequest_SetsCancelled()
    {
        using var ctx = CreateContext();
        var request = ApprovalsController.CreateApprovalRequest(
            "Employee", Guid.NewGuid(), "test", null, "user");
        ctx.ApprovalRequests.Add(request);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx);

        await controller.Cancel(request.Id);

        var updated = await ctx.ApprovalRequests.FirstAsync(r => r.Id == request.Id);
        Assert.Equal(ApprovalStatus.Cancelled, updated.Status);
    }

    [Fact]
    public async Task Approve_AlreadyApproved_ReturnsRedirectWithError()
    {
        using var ctx = CreateContext();
        var request = ApprovalsController.CreateApprovalRequest(
            "Employee", Guid.NewGuid(), "test", null, "user");
        request.Status = ApprovalStatus.Approved;
        ctx.ApprovalRequests.Add(request);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx);

        var result = await controller.Approve(request.Id, "test");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("ليس في حالة انتظار", (string)controller.TempData["Error"]!);
    }

    [Fact]
    public async Task Index_ReturnsPaginatedResults()
    {
        using var ctx = CreateContext();
        for (int i = 0; i < 5; i++)
        {
            ctx.ApprovalRequests.Add(ApprovalsController.CreateApprovalRequest(
                "Employee", Guid.NewGuid(), $"request {i}", null, "user"));
        }
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx);

        var result = await controller.Index(null, null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.NotNull(viewResult.Model);
    }

    [Fact]
    public async Task Pending_ReturnsOnlyPendingRequests()
    {
        using var ctx = CreateContext();
        var pending = ApprovalsController.CreateApprovalRequest("Employee", Guid.NewGuid(), "pending", null, "u");
        var approved = ApprovalsController.CreateApprovalRequest("Employee", Guid.NewGuid(), "approved", null, "u");
        approved.Status = ApprovalStatus.Approved;

        ctx.ApprovalRequests.AddRange(pending, approved);
        await ctx.SaveChangesAsync();

        var controller = CreateController(ctx);

        var result = await controller.Pending();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<List<ApprovalRequest>>(viewResult.Model);
        Assert.Single(model);
        Assert.Equal(ApprovalStatus.Pending, model.First().Status);
    }

    [Fact]
    public void ApprovalRequest_DefaultStatus_IsPending()
    {
        var request = new ApprovalRequest();
        Assert.Equal(ApprovalStatus.Pending, request.Status);
        Assert.Equal(1, request.CurrentLevel);
    }

    [Fact]
    public void ApprovalStep_DefaultStatus_IsPending()
    {
        var step = new ApprovalStep();
        Assert.Equal(ApprovalStatus.Pending, step.Status);
    }
}
