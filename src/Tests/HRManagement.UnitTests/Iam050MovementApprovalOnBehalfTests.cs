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

    private static ApprovalActing OnBehalf(Guid? principalScope = null, Guid? principalEmployee = null, Guid? delegateEmployee = null) =>
        new(Delegate, delegateEmployee, false,
            new ActiveDelegation(Guid.NewGuid(), Principal, principalEmployee, "الأصيل", HrManagementRoles.HrManagementApprover, null, DateTime.UtcNow.AddDays(1), principalScope));

    private static readonly ApprovalActing Direct = new(Delegate, null, true, null);
    private static readonly ApprovalActing Nobody = new(Delegate, null, false, null);

    private static MovementsDbContext Db() =>
        new(new DbContextOptionsBuilder<MovementsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ClaimsPrincipal User(string role = HrManagementRoles.HrCore) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Delegate.ToString("D")), new Claim(ClaimTypes.Role, role)], "Test"));

    private static T With<T>(T controller, string role = HrManagementRoles.HrCore) where T : Controller
    {
        var http = new DefaultHttpContext { User = User(role) };
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

    // ---- review 3: the subject rule on behalf ----

    [Fact]
    public async Task A_delegate_does_not_approve_a_movement_of_his_principal_or_of_himself()
    {
        await using var db = Db();
        var principalEmployee = Guid.NewGuid();
        var delegateEmployee = Guid.NewGuid();
        foreach (var subject in new[] { principalEmployee, delegateEmployee })
        {
            var transfer = await TransferAsync(db, Creator);
            transfer.EmployeeId = subject;
            await db.SaveChangesAsync();

            var approve = Transfers(db, OnBehalf(principalEmployee: principalEmployee, delegateEmployee: delegateEmployee));
            await approve.Approve(transfer.Id);
            Assert.Equal(MovementApprovalActing.SubjectMessage, approve.TempData["Error"]);
            var reject = Transfers(db, OnBehalf(principalEmployee: principalEmployee, delegateEmployee: delegateEmployee));
            await reject.Reject(transfer.Id, "x");
            Assert.Equal(MovementApprovalActing.SubjectMessage, reject.TempData["Error"]);
            Assert.Equal(ApprovalStatus.Pending, (await db.TransferRequests.SingleAsync(t => t.Id == transfer.Id)).Status);
        }

        // A holder of the direct role is not touched by the on-behalf rule.
        Assert.Null(MovementApprovalActing.Subject(Direct, delegateEmployee));
    }

    [Fact]
    public async Task A_promotion_of_the_principal_is_not_approved_on_behalf()
    {
        await using var db = Db();
        var principalEmployee = Guid.NewGuid();
        var promotion = new PromotionRequest
        {
            EmployeeId = principalEmployee,
            TransitionDecisionRef = "IAM50-UNIT",
            Status = PromotionRequestStatus.Pending,
            RequestedBy = Creator
        };
        db.PromotionRequests.Add(promotion);
        await db.SaveChangesAsync();

        var controller = With(new PromotionRequestsController(db, Mock.Of<IPublishEndpoint>(), new FakeHrCoreActiveRegimeClient(), new FixedActing(OnBehalf(principalEmployee: principalEmployee))));
        await controller.Approve(promotion.Id);
        Assert.Equal(MovementApprovalActing.SubjectMessage, controller.TempData["Error"]);
        Assert.Equal(PromotionRequestStatus.Pending, (await db.PromotionRequests.SingleAsync(p => p.Id == promotion.Id)).Status);
    }

    // ---- review 4: without the acting context, only a direct role approves ----

    [Fact]
    public async Task Without_the_acting_context_a_caller_without_a_direct_role_is_refused()
    {
        await using var db = Db();
        var transfer = await TransferAsync(db, Creator);
        var noRole = With(new TransferRequestsController(db, Mock.Of<IPublishEndpoint>(), new FakeHrCoreActiveRegimeClient()), HrManagementRoles.HrMovements);
        Assert.IsType<ForbidResult>(await noRole.Approve(transfer.Id));
        Assert.Equal(ApprovalStatus.Pending, (await db.TransferRequests.SingleAsync(t => t.Id == transfer.Id)).Status);

        var approver = With(new TransferRequestsController(db, Mock.Of<IPublishEndpoint>(), new FakeHrCoreActiveRegimeClient()), HrManagementRoles.HrManagementApprover);
        await approver.Approve(transfer.Id);
        Assert.Equal(ApprovalStatus.Approved, (await db.TransferRequests.SingleAsync(t => t.Id == transfer.Id)).Status);
    }

    // ---- review 1: a reader admitted through a delegation reads inside the principal's stored scope ----

    [Fact]
    public async Task A_reader_admitted_by_a_delegation_reads_inside_the_principals_branch_only()
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
        var inside = await TransferAsync(db, Creator, branch.Id, child.Id);
        var outside = await TransferAsync(db, Creator, branch.Id, elsewhere.Id);

        TransferRequestsController Reader(Guid? principalScope)
        {
            var controller = Transfers(db, Nobody);
            controller.HttpContext.Items[ApprovalActingContext.ItemKey] = OnBehalf(principalScope).OnBehalfOf;
            return controller;
        }

        var list = Assert.IsAssignableFrom<IEnumerable<TransferRequest>>(Assert.IsType<ViewResult>(await Reader(branch.Id).Index("all")).Model);
        Assert.Equal([inside.Id], list.Select(t => t.Id).ToArray());
        Assert.IsType<ViewResult>(await Reader(branch.Id).Details(inside.Id));
        Assert.IsType<ForbidResult>(await Reader(branch.Id).Details(outside.Id));

        // A central principal (no stored scope) sets no limit beyond the reader's own.
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<TransferRequest>>(Assert.IsType<ViewResult>(await Reader(null).Index("all")).Model).Count());
        Assert.IsType<ViewResult>(await Reader(null).Details(outside.Id));
    }

    [Fact]
    public async Task A_reader_admitted_by_a_delegation_reads_loans_inside_the_principals_branch_only()
    {
        await using var db = Db();
        var branch = new OrgUnitSnapshot { Id = Guid.NewGuid(), TenantId = Guid.Empty, Code = "B1", NameAr = "B1" };
        branch.MaterializedPath = $"/{branch.Id:D}/";
        var child = new OrgUnitSnapshot { Id = Guid.NewGuid(), TenantId = Guid.Empty, Code = "B1a", NameAr = "B1a", ParentId = branch.Id };
        child.MaterializedPath = $"{branch.MaterializedPath}{child.Id:D}/";
        var elsewhere = new OrgUnitSnapshot { Id = Guid.NewGuid(), TenantId = Guid.Empty, Code = "B2", NameAr = "B2" };
        elsewhere.MaterializedPath = $"/{elsewhere.Id:D}/";
        db.OrgUnitSnapshots.AddRange(branch, child, elsewhere);
        LoanAssignmentRequest Loan(Guid borrowing) => new()
        {
            EmployeeId = Guid.NewGuid(),
            LendingOrgUnitId = branch.Id,
            BorrowingOrgUnitId = borrowing,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Status = LoanAssignmentStatus.Pending,
            RequestedBy = Creator
        };
        var inside = Loan(child.Id);
        var outside = Loan(elsewhere.Id);
        db.LoanAssignmentRequests.AddRange(inside, outside);
        await db.SaveChangesAsync();

        LoanAssignmentRequestsController Reader(Guid? principalScope)
        {
            var controller = With(new LoanAssignmentRequestsController(db, Mock.Of<IPublishEndpoint>(), new FixedActing(Nobody)));
            controller.HttpContext.Items[ApprovalActingContext.ItemKey] = OnBehalf(principalScope).OnBehalfOf;
            return controller;
        }

        var list = Assert.IsAssignableFrom<IEnumerable<LoanAssignmentRequest>>(Assert.IsType<ViewResult>(await Reader(branch.Id).Index("all")).Model);
        Assert.Equal([inside.Id], list.Select(l => l.Id).ToArray());
        Assert.IsType<ViewResult>(await Reader(branch.Id).Details(inside.Id));
        Assert.IsType<ForbidResult>(await Reader(branch.Id).Details(outside.Id));
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<LoanAssignmentRequest>>(Assert.IsType<ViewResult>(await Reader(null).Index("all")).Model).Count());
    }

    [Fact]
    public async Task The_subject_rule_holds_on_secondments_loans_and_assignments()
    {
        await using var db = Db();
        var principalEmployee = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6));
        var secondment = new SecondmentRequest
        {
            EmployeeId = principalEmployee,
            HostOrg = "جهة",
            StartDate = start,
            EndDate = end,
            Status = SecondmentStatus.Pending,
            RequestedBy = Creator
        };
        var loan = new LoanAssignmentRequest
        {
            EmployeeId = principalEmployee,
            LendingOrgUnitId = Guid.NewGuid(),
            BorrowingOrgUnitId = Guid.NewGuid(),
            StartDate = start,
            EndDate = end,
            Status = LoanAssignmentStatus.Pending,
            RequestedBy = Creator
        };
        var assignment = new DelegationRequest
        {
            EmployeeId = principalEmployee,
            HostOrg = "جهة",
            StartDate = start,
            EndDate = end,
            Status = DelegationRequestStatus.Pending,
            RequestedBy = Creator
        };
        db.SecondmentRequests.Add(secondment);
        db.LoanAssignmentRequests.Add(loan);
        db.DelegationRequests.Add(assignment);
        await db.SaveChangesAsync();
        var acting = new FixedActing(OnBehalf(principalEmployee: principalEmployee));

        var secondments = With(new SecondmentRequestsController(db, Mock.Of<IPublishEndpoint>(), acting));
        await secondments.Approve(secondment.Id);
        Assert.Equal(MovementApprovalActing.SubjectMessage, secondments.TempData["Error"]);
        var loans = With(new LoanAssignmentRequestsController(db, Mock.Of<IPublishEndpoint>(), acting));
        await loans.Approve(loan.Id);
        Assert.Equal(MovementApprovalActing.SubjectMessage, loans.TempData["Error"]);
        var assignments = With(new DelegationRequestsController(db, Mock.Of<IPublishEndpoint>(), acting));
        await assignments.Approve(assignment.Id);
        Assert.Equal(MovementApprovalActing.SubjectMessage, assignments.TempData["Error"]);

        Assert.Equal(SecondmentStatus.Pending, (await db.SecondmentRequests.SingleAsync(s => s.Id == secondment.Id)).Status);
        Assert.Equal(LoanAssignmentStatus.Pending, (await db.LoanAssignmentRequests.SingleAsync(l => l.Id == loan.Id)).Status);
        Assert.Equal(DelegationRequestStatus.Pending, (await db.DelegationRequests.SingleAsync(d => d.Id == assignment.Id)).Status);
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
