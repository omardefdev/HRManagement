using System.Security.Claims;
using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Domain.Services;
using HR.Movements.Infrastructure.Data;
using HR.Movements.Web;
using HR.Movements.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.TestSupport.Integration;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-50 (§7, P-7): the movement approvals with a delegation. A delegate approves on behalf, and the transfer's approval record and the
/// checklist's approval step name the principal; neither the delegate's own request nor his principal's is approved
/// («لا يجوز اعتماد طلب أنشأه المعتمد أو أصيله»); the principal's stored branch bounds the delegate; a lookup that failed (no delegation for a
/// user without the role) stops him; a holder of the direct role approves in his own name.
/// </summary>
public class Iam050MovementApprovalOnBehalfTests
{
    private static readonly Guid Principal = Guid.Parse("50f00000-0000-4000-8000-000000000001");
    private static readonly Guid Delegate = Guid.Parse("50f00000-0000-4000-8000-000000000002");
    private static readonly Guid Creator = Guid.Parse("50f00000-0000-4000-8000-000000000003");

    private sealed class FixedActing(ApprovalActing acting) : IApprovalActingContext
    {
        public string? AskedType { get; private set; }

        public Task<ApprovalActing> GetAsync(IReadOnlyCollection<string> directRoles, string delegatedRole, string entityType, CancellationToken ct = default)
        {
            AskedType = entityType;
            return Task.FromResult(acting);
        }
    }

    private static ApprovalActing OnBehalf(Guid? principalScope = null) =>
        new(Delegate, null, false,
            new ActiveDelegation(Guid.NewGuid(), Principal, null, "الأصيل", HrManagementRoles.HrManagementApprover, null, DateTime.UtcNow.AddDays(1), principalScope));

    private static readonly ApprovalActing Direct = new(Delegate, null, true, null);
    private static readonly ApprovalActing Nobody = new(Delegate, null, false, null);

    private static MovementsDbContext Db() =>
        new(new DbContextOptionsBuilder<MovementsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ClaimsPrincipal User() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Delegate.ToString("D")), new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)], "Test"));

    private static T With<T>(T controller) where T : Controller
    {
        var http = new DefaultHttpContext { User = User() };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static async Task<TransferRequest> TransferAsync(MovementsDbContext db, Guid requestedBy, Guid? from = null, Guid? to = null)
    {
        var transfer = new TransferRequest
        {
            EmployeeId = Guid.NewGuid(),
            FromOrgUnitId = from ?? Guid.NewGuid(),
            ToOrgUnitId = to ?? Guid.NewGuid(),
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = ApprovalStatus.Pending,
            RequestedBy = requestedBy
        };
        db.TransferRequests.Add(transfer);
        foreach (var step in MovementChecklistDefaults.CreateFor(MovementKind.Transfer, transfer.Id))
            db.MovementChecklists.Add(step);
        await db.SaveChangesAsync();
        return transfer;
    }

    private static TransferRequestsController Transfers(MovementsDbContext db, ApprovalActing acting) =>
        With(new TransferRequestsController(db, Mock.Of<IPublishEndpoint>(), new FakeHrCoreActiveRegimeClient(), new FixedActing(acting)));

    [Fact]
    public async Task A_delegate_approves_a_transfer_on_behalf_and_the_records_name_the_principal()
    {
        await using var db = Db();
        var transfer = await TransferAsync(db, Creator);
        var acting = new FixedActing(OnBehalf());
        var controller = With(new TransferRequestsController(db, Mock.Of<IPublishEndpoint>(), new FakeHrCoreActiveRegimeClient(), acting));

        Assert.IsType<RedirectToActionResult>(await controller.Approve(transfer.Id));

        Assert.Null(controller.TempData["Error"]);
        Assert.Equal(TransferRequestsController.EntityType, acting.AskedType);
        var stored = await db.TransferRequests.SingleAsync(t => t.Id == transfer.Id);
        Assert.Equal(ApprovalStatus.Approved, stored.Status);
        var approval = await db.TransferApprovals.SingleAsync(a => a.TransferRequestId == transfer.Id);
        Assert.Equal(Delegate, approval.ApprovedBy);
        Assert.Equal(Principal, approval.OnBehalfOfUserId);
        Assert.Equal("الأصيل", approval.OnBehalfOfName);
        var step = await db.MovementChecklists.SingleAsync(c => c.CorrelationId == transfer.Id && c.Step == MovementChecklistStep.Approved);
        Assert.Equal(Delegate, step.CompletedBy);
        Assert.Equal(Principal, step.OnBehalfOfUserId);
    }

    [Fact]
    public async Task A_holder_of_the_direct_role_approves_in_his_own_name()
    {
        await using var db = Db();
        var transfer = await TransferAsync(db, Creator);
        await Transfers(db, Direct).Approve(transfer.Id);
        var approval = await db.TransferApprovals.SingleAsync(a => a.TransferRequestId == transfer.Id);
        Assert.Null(approval.OnBehalfOfUserId);
        Assert.Null(approval.OnBehalfOfName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Neither_the_delegates_own_request_nor_his_principals_is_approved(bool byPrincipal)
    {
        await using var db = Db();
        var transfer = await TransferAsync(db, byPrincipal ? Principal : Delegate);
        var controller = Transfers(db, OnBehalf());

        await controller.Approve(transfer.Id);

        Assert.Equal(MovementApprovalActing.CreatorOrPrincipalMessage, controller.TempData["Error"]);
        Assert.Equal(ApprovalStatus.Pending, (await db.TransferRequests.SingleAsync(t => t.Id == transfer.Id)).Status);
        Assert.Empty(db.TransferApprovals);

        var reject = Transfers(db, OnBehalf());
        await reject.Reject(transfer.Id, "x");
        Assert.Equal(MovementApprovalActing.CreatorOrPrincipalMessage, reject.TempData["Error"]);
        Assert.Equal(ApprovalStatus.Pending, (await db.TransferRequests.SingleAsync(t => t.Id == transfer.Id)).Status);
    }

    [Fact]
    public async Task Without_a_delegation_or_after_a_failed_lookup_the_delegate_is_refused()
    {
        await using var db = Db();
        var transfer = await TransferAsync(db, Creator);
        Assert.IsType<ForbidResult>(await Transfers(db, Nobody).Approve(transfer.Id));
        Assert.IsType<ForbidResult>(await Transfers(db, Nobody).Reject(transfer.Id, null));
        Assert.Equal(ApprovalStatus.Pending, (await db.TransferRequests.SingleAsync(t => t.Id == transfer.Id)).Status);
    }

    [Fact]
    public async Task The_principals_stored_branch_bounds_the_delegate()
    {
        await using var db = Db();
        var branch = new OrgUnitSnapshot { Id = Guid.NewGuid(), TenantId = Guid.Empty, Code = "B1", NameAr = "B1" };
        branch.MaterializedPath = $"/{branch.Id:D}/";
        var child = new OrgUnitSnapshot { Id = Guid.NewGuid(), TenantId = Guid.Empty, Code = "B1a", NameAr = "B1a", ParentId = branch.Id };
        child.MaterializedPath = $"{branch.MaterializedPath}{child.Id:D}/";
        var elsewhere = new OrgUnitSnapshot { Id = Guid.NewGuid(), TenantId = Guid.Empty, Code = "B2", NameAr = "B2" };
        elsewhere.MaterializedPath = $"/{elsewhere.Id:D}/";
        db.OrgUnitSnapshots.AddRange(branch, child, elsewhere);
        await db.SaveChangesAsync();

        var outside = await TransferAsync(db, Creator, branch.Id, elsewhere.Id);
        Assert.IsType<ForbidResult>(await Transfers(db, OnBehalf(principalScope: branch.Id)).Approve(outside.Id));
        Assert.Equal(ApprovalStatus.Pending, (await db.TransferRequests.SingleAsync(t => t.Id == outside.Id)).Status);

        var inside = await TransferAsync(db, Creator, branch.Id, child.Id);
        await Transfers(db, OnBehalf(principalScope: branch.Id)).Approve(inside.Id);
        Assert.Equal(ApprovalStatus.Approved, (await db.TransferRequests.SingleAsync(t => t.Id == inside.Id)).Status);
    }

    [Fact]
    public async Task A_secondment_approved_on_behalf_names_the_principal_on_its_checklist()
    {
        await using var db = Db();
        var secondment = new SecondmentRequest
        {
            EmployeeId = Guid.NewGuid(),
            HostOrg = "جهة",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            Status = SecondmentStatus.Pending,
            RequestedBy = Creator
        };
        db.SecondmentRequests.Add(secondment);
        foreach (var step in MovementChecklistDefaults.CreateFor(MovementKind.Secondment, secondment.Id))
            db.MovementChecklists.Add(step);
        await db.SaveChangesAsync();
        var acting = new FixedActing(OnBehalf());

        await With(new SecondmentRequestsController(db, Mock.Of<IPublishEndpoint>(), acting)).Approve(secondment.Id);

        Assert.Equal("SecondmentRequest", acting.AskedType);
        Assert.Equal(SecondmentStatus.Approved, (await db.SecondmentRequests.SingleAsync(s => s.Id == secondment.Id)).Status);
        var step2 = await db.MovementChecklists.SingleAsync(c => c.CorrelationId == secondment.Id && c.Step == MovementChecklistStep.Approved);
        Assert.Equal(Principal, step2.OnBehalfOfUserId);
    }

    [Fact]
    public void Each_of_the_five_controllers_names_its_entity_type()
    {
        foreach (var (controller, type) in new[]
                 {
                     (typeof(TransferRequestsController), "TransferRequest"),
                     (typeof(SecondmentRequestsController), "SecondmentRequest"),
                     (typeof(LoanAssignmentRequestsController), "LoanAssignmentRequest"),
                     (typeof(PromotionRequestsController), "PromotionRequest"),
                     (typeof(DelegationRequestsController), "DelegationRequest")
                 })
        {
            var attribute = (ApprovalEntityTypeAttribute?)Attribute.GetCustomAttribute(controller, typeof(ApprovalEntityTypeAttribute));
            Assert.Equal(type, attribute?.EntityType);
            Assert.Contains(type, DelegationOptions.DefaultEntityTypes);
        }
    }
}
