using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using static HRManagement.UnitTests.Wf023WorkInboxUiTests;

namespace HRManagement.UnitTests;

/// <summary>PLAN-44 (G-WF-19): the legacy path of ApprovedCV checks the step's role, and only the requester withdraws a request.</summary>
public sealed class Wf044LegacyApprovalRoleTests
{
    private static readonly Guid Requester = Guid.Parse("44000000-0000-4000-8000-0000000000e1");
    private static readonly Guid Reviewer = Guid.Parse("44000000-0000-4000-8000-0000000000e2");

    private static ApprovalRequest Request() => ApprovalWorkflowService.CreateApprovalRequest(
        "ApprovedCV", Guid.NewGuid(), "x", Requester, "r", requiredRoles: [HrManagementRoles.HrCore, HrManagementRoles.HrManagementApprover]);

    [Fact]
    public void An_approval_without_the_steps_role_is_refused_and_changes_nothing()
    {
        var request = Request();

        var ok = ApprovalWorkflowService.TryApproveStep(request, Reviewer, "r", null, [HrManagementRoles.HrManagementApprover], false, out var error);

        Assert.False(ok);
        Assert.Equal($"هذه الخطوة تتطلب دور «{HrManagementRoles.HrCore}».", error);
        Assert.Equal(1, request.CurrentLevel);
        Assert.All(request.Steps, s => Assert.Equal(ApprovalStatus.Pending, s.Status));
    }

    [Fact]
    public void A_rejection_without_the_steps_role_is_refused_and_changes_nothing()
    {
        var request = Request();

        var ok = ApprovalWorkflowService.TryReject(request, Reviewer, "r", null, ["auditor"], false, out var error);

        Assert.False(ok);
        Assert.Contains(HrManagementRoles.HrCore, error!);
        Assert.Equal(ApprovalStatus.Pending, request.Status);
    }

    [Fact]
    public void The_steps_role_passes_whatever_its_case_and_the_next_step_asks_for_its_own()
    {
        var request = Request();

        Assert.True(ApprovalWorkflowService.TryApproveStep(request, Reviewer, "r", null, [HrManagementRoles.HrCore.ToUpperInvariant()], false, out _));
        Assert.Equal(2, request.CurrentLevel);

        Assert.False(ApprovalWorkflowService.TryApproveStep(request, Guid.NewGuid(), "s", null, [HrManagementRoles.HrCore], false, out var error));
        Assert.Contains(HrManagementRoles.HrManagementApprover, error!);
    }

    [Fact]
    public void An_admin_passes_only_with_the_bypass_and_the_maker_rule_still_holds()
    {
        var request = Request();

        Assert.False(ApprovalWorkflowService.TryApproveStep(request, Reviewer, "a", null, [HrManagementRoles.Admin], false, out _));
        Assert.False(ApprovalWorkflowService.TryApproveStep(request, Requester, "a", null, [HrManagementRoles.Admin], true, out var maker));
        Assert.Contains("منشئ", maker!);
        Assert.True(ApprovalWorkflowService.TryApproveStep(request, Reviewer, "a", null, [HrManagementRoles.Admin], true, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_inbox_offers_an_admin_a_step_exactly_when_the_server_lets_it_through(bool adminBypass)
    {
        await using var db = CreateContext();
        var request = Request();
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync();
        var query = new WorkflowInboxQueryService(
            db,
            new WorkflowInboxEligibilityService(new WorkflowStepRoleOptions { AdminBypass = adminBypass }),
            new WorkflowDeepLinkResolver(db),
            WorkflowInboxTestSupport.CreateOrgScopeService(db));

        var items = await query.GetActionableLegacyItemsAsync(CreateUser(Reviewer, HrManagementRoles.Admin));

        Assert.Equal(adminBypass, items.Count == 1);
        Assert.Equal(adminBypass, ApprovalWorkflowService.TryApproveStep(request, Reviewer, "a", null, [HrManagementRoles.Admin], adminBypass, out _));
    }

    private static ApprovalsController Controller(HR.Core.Infrastructure.Data.HRCoreDbContext db, ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { User = user };
        return new ApprovalsController(db, new WorkflowStepRoleOptions())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };
    }

    private static async Task<(ApprovalStatus Status, string? Error)> CancelAsync(Guid actor, string role)
    {
        await using var db = CreateContext();
        var request = Request();
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync();
        var controller = Controller(db, CreateUser(actor, role));

        await controller.Cancel(request.Id);

        var status = (await db.ApprovalRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status;
        return (status, controller.TempData["Error"] as string);
    }

    [Fact]
    public async Task The_requester_cancels()
    {
        Assert.Equal(ApprovalStatus.Cancelled, (await CancelAsync(Requester, HrManagementRoles.HrCore)).Status);
    }

    [Fact]
    public async Task An_admin_cancels()
    {
        Assert.Equal(ApprovalStatus.Cancelled, (await CancelAsync(Reviewer, HrManagementRoles.Admin)).Status);
    }

    [Fact]
    public async Task A_colleague_with_write_access_does_not()
    {
        var (status, error) = await CancelAsync(Reviewer, HrManagementRoles.HrCore);

        Assert.Equal(ApprovalStatus.Pending, status);
        Assert.Equal(ApprovalsController.NotRequesterMessage, error);
    }
}
