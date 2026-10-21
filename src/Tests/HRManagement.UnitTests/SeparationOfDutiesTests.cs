using HR.Core.Domain.Services;
using HR.Core.Web.Controllers;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.SeparationOfDuties;

namespace HRManagement.UnitTests;

public class SeparationOfDutiesTests
{
    private static SeparationOfDutiesService CreateSod(
        IEnumerable<ApprovalDelegation>? delegations = null) =>
        new(delegations: delegations);

    [Fact]
    public void SodService_DeniesCreatorAsApprover()
    {
        var creator = Guid.NewGuid();
        var sod = CreateSod();

        var result = sod.ValidateAction(
            creator, "payroll-approver", "PayrollRun", Guid.NewGuid(),
            SodAction.Check, creator, null);

        Assert.False(result.Allowed);
        Assert.Contains("منشئ", result.DenialReason);
    }

    [Fact]
    public void SodService_AllowsValidApproval()
    {
        var creator = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var sod = CreateSod();

        var result = sod.ValidateAction(
            approver, "payroll-approver", "PayrollRun", Guid.NewGuid(),
            SodAction.Check, creator, null);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void SodService_DeniesApproverAsPoster()
    {
        var creator = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var sod = CreateSod();

        var result = sod.ValidateAction(
            approver, "payroll-poster", "PayrollRun", Guid.NewGuid(),
            SodAction.Post, creator, approver);

        Assert.False(result.Allowed);
        Assert.Contains("ترحيل", result.DenialReason!);
    }

    [Fact]
    public void SodService_DeniesPaymentBatchDisburseForRunApprover()
    {
        var creator = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var poster = approver;
        var sod = CreateSod();

        var result = sod.ValidateAction(
            poster, "payroll-poster", "PaymentBatch", Guid.NewGuid(),
            SodAction.Post, creator, approver);

        Assert.False(result.Allowed);
    }

    [Fact]
    public void SodMatrix_includes_PaymentBatch_rule()
    {
        var rule = SodMatrixDefaults.CreateDefaultRules()
            .FirstOrDefault(r => r.EntityType == "PaymentBatch");
        Assert.NotNull(rule);
        Assert.Equal("payroll-approver", rule!.CheckerRole);
        Assert.Equal("payroll-poster", rule.PosterRole);
    }

    [Fact]
    public void SodService_DeniesConflictingRoleAssignment()
    {
        var sod = CreateSod();
        var result = sod.ValidateRoleAssignment(
            Guid.NewGuid(),
            ["payroll-processor"],
            "payroll-approver");

        Assert.False(result.Allowed);
        Assert.Contains("الرواتب", result.DenialReason!);
    }

    [Fact]
    public void SodService_RequiresDualApprovalAboveThreshold()
    {
        var sod = CreateSod();
        Assert.Equal(1, sod.GetRequiredApprovalCount("PayrollRun", 1_000m));
        Assert.Equal(2, sod.GetRequiredApprovalCount("PayrollRun", SodMatrixDefaults.DefaultPayrollThreshold + 1));
        Assert.True(sod.RequiresDualApproval("RoleAssignment"));
        Assert.True(sod.RequiresDualApproval("LegalDataChange"));
    }

    [Fact]
    public void SodService_ResolvesActiveDelegate()
    {
        var principal = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var sod = CreateSod(
        [
            new ApprovalDelegation
            {
                PrincipalId = principal,
                DelegateId = delegateId,
                ActiveFrom = now.AddDays(-1),
                ActiveUntil = now.AddDays(7),
                Reason = "إجازة",
                EntityTypeScope = "LeaveRequest"
            }
        ]);

        Assert.Equal(delegateId, sod.ResolveDelegate(principal, "LeaveRequest", now));
        Assert.True(sod.IsActingAsDelegate(delegateId, principal, "LeaveRequest", now));
        Assert.Null(sod.ResolveDelegate(principal, "PayrollRun", now));
    }

    [Fact]
    public void SodService_RejectsExpiredDelegation()
    {
        var principal = Guid.NewGuid();
        var delegateId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var sod = CreateSod(
        [
            new ApprovalDelegation
            {
                PrincipalId = principal,
                DelegateId = delegateId,
                ActiveFrom = now.AddDays(-30),
                ActiveUntil = now.AddDays(-1),
                Reason = "انتهى"
            }
        ]);

        Assert.Null(sod.ResolveDelegate(principal, "LeaveRequest", now));
    }

    [Fact]
    public void SodService_RejectsSelfDelegation()
    {
        var user = Guid.NewGuid();
        var result = SeparationOfDutiesService.ValidateDelegation(new ApprovalDelegation
        {
            PrincipalId = user,
            DelegateId = user,
            ActiveFrom = DateTime.UtcNow,
            ActiveUntil = DateTime.UtcNow.AddDays(7),
            Reason = "اختبار"
        });

        Assert.False(result.Allowed);
    }

    [Fact]
    public void SodService_RejectsDelegationLongerThan90Days()
    {
        var result = SeparationOfDutiesService.ValidateDelegation(new ApprovalDelegation
        {
            PrincipalId = Guid.NewGuid(),
            DelegateId = Guid.NewGuid(),
            ActiveFrom = DateTime.UtcNow,
            ActiveUntil = DateTime.UtcNow.AddDays(91),
            Reason = "طويل"
        });

        Assert.False(result.Allowed);
        Assert.Contains("90", result.DenialReason!);
    }

    [Fact]
    public void SodService_RejectsSelfRoleEscalation()
    {
        var user = Guid.NewGuid();
        var sod = CreateSod();
        var result = sod.ValidateRoleAssignment(user, ["hr-core"], "hr-admin", assignorId: user);
        Assert.False(result.Allowed);
    }

    [Fact]
    public void RoleAssignmentGuard_BlocksSelfGrant()
    {
        var user = Guid.NewGuid();
        var result = RoleAssignmentGuard.ValidateGrant(
            user, ["hr-admin"], user, [], "hr-core");
        Assert.False(result.Allowed);
    }

    [Fact]
    public void RoleAssignmentGuard_BlocksConflictingRole()
    {
        var result = RoleAssignmentGuard.ValidateGrant(
            Guid.NewGuid(),
            ["hr-admin"],
            Guid.NewGuid(),
            ["payroll-processor"],
            "payroll-approver");
        Assert.False(result.Allowed);
    }

    [Fact]
    public void RoleAssignmentGuard_BlocksPrivilegedWithoutDual()
    {
        var result = RoleAssignmentGuard.ValidateGrant(
            Guid.NewGuid(),
            ["platform-admin"],
            Guid.NewGuid(),
            [],
            "hr-admin");
        Assert.False(result.Allowed);
        Assert.Contains("مزدوجاً", result.DenialReason!);
    }

    [Fact]
    public void RoleAssignmentGuard_AllowsPrivilegedWithDualApprovers()
    {
        var result = RoleAssignmentGuard.ValidatePrivilegedGrant(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            [],
            "hr-admin");
        Assert.True(result.Allowed);
    }

    [Fact]
    public void RoleAssignmentGuard_BlocksHigherRankGrant()
    {
        var result = RoleAssignmentGuard.ValidateGrant(
            Guid.NewGuid(),
            ["hr-core"],
            Guid.NewGuid(),
            [],
            "hr-management-approver");
        Assert.False(result.Allowed);
        Assert.Contains("أعلى", result.DenialReason!);
    }

    [Fact]
    public void ApprovalWorkflowService_BlocksSelfApprove()
    {
        var creator = Guid.NewGuid();
        var request = ApprovalsController.CreateApprovalRequest(
            "Employee", Guid.NewGuid(), "test", creator, "maker");

        var ok = ApprovalWorkflowService.TryApproveStep(request, creator, "maker", null, out var error);
        Assert.False(ok);
        Assert.Contains("منشئ", error!);
    }

    [Fact]
    public void ApprovalWorkflowService_FinancialEscalation_AddsSecondStep()
    {
        var request = ApprovalsController.CreateApprovalRequest(
            "PayrollRun",
            Guid.NewGuid(),
            "high amount",
            Guid.NewGuid(),
            "processor",
            financialAmount: SodMatrixDefaults.DefaultPayrollThreshold + 1);

        Assert.Equal(2, request.Steps.Count);
        Assert.Contains(request.Steps, s => s.RoleRequired == "hr-management-approver");
    }

    [Fact]
    public void ApprovalWorkflowService_BlocksSameApproverOnSecondStep()
    {
        var creator = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var request = ApprovalsController.CreateApprovalRequest(
            "LegalDataChange",
            Guid.NewGuid(),
            "legal",
            creator,
            "maker",
            requiredRoles: ["hr-core", "hr-management-approver"]);

        Assert.True(ApprovalWorkflowService.TryApproveStep(request, approver, "a1", null, out _));
        Assert.Equal(ApprovalStatus.Pending, request.Status);

        var second = ApprovalWorkflowService.TryApproveStep(request, approver, "a1", null, out var error);
        Assert.False(second);
        Assert.Contains("ثانٍ", error!);
    }

    [Fact]
    public void LegalDataChangeGuard_RequiresSecondApprover()
    {
        var actor = Guid.NewGuid();
        var denied = LegalDataChangeGuard.ValidateChange(actor, null, ["NationalId"]);
        Assert.False(denied.Allowed);

        var allowed = LegalDataChangeGuard.ValidateChange(actor, Guid.NewGuid(), ["NationalId"]);
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public void GenerateViolationReport_FindsConflicts()
    {
        var sod = CreateSod();
        var reports = sod.GenerateViolationReport(
        [
            new UserRoleSnapshot(Guid.NewGuid(), "ali", ["payroll-processor", "payroll-approver"])
        ]).ToList();

        Assert.Single(reports);
        Assert.Equal("payroll-processor", reports[0].RoleA);
    }
}
