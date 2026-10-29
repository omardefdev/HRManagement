using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Workflow;

namespace HRManagement.UnitTests;

/// <summary>PLAN-25 (G-04): the server-side step-role check of <see cref="WorkflowStageGuard"/>.</summary>
public sealed class WorkflowStageGuardRoleTests
{
    private const string Enforced = "RecruitmentRequisition";

    private static readonly WorkflowDefinitionSnapshot Definition = new()
    {
        Code = "RECRUITMENT-REQUISITION-APPROVAL",
        Steps =
        [
            new WorkflowStepSnapshot { Level = 1, Name = "مراجعة", RoleRequired = HrManagementRoles.HrCore },
            new WorkflowStepSnapshot { Level = 2, Name = "اعتماد", RoleRequired = HrManagementRoles.HrManagementApprover }
        ]
    };

    private static WorkflowInstanceState State(int level, WorkflowStatus status = WorkflowStatus.InProgress) =>
        new() { Id = Guid.NewGuid(), CurrentStepLevel = level, Status = status };

    private static IReadOnlySet<string> Roles(params string[] roles) => roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string? Approve(int level, IReadOnlySet<string> roles, WorkflowStepRoleOptions? options = null, string entityType = Enforced,
        Guid? actor = null, Guid? initiator = null) =>
        WorkflowStageGuard.ValidateApprove(State(level), Definition, actor ?? Guid.NewGuid(), initiator, level, entityType, roles,
            options ?? new WorkflowStepRoleOptions());

    [Fact]
    public void Role_present_passes() =>
        Assert.Null(Approve(2, Roles(HrManagementRoles.HrManagementApprover)));

    [Fact]
    public void Role_absent_is_refused_with_the_arabic_role_message() =>
        Assert.Equal("هذه الخطوة تتطلب دور «hr-management-approver».", Approve(2, Roles(HrManagementRoles.HrCore)));

    [Fact]
    public void Any_of_several_roles_counts_and_the_comparison_ignores_case()
    {
        Assert.Null(Approve(2, Roles(HrManagementRoles.BranchManager, HrManagementRoles.HrCore, "HR-MANAGEMENT-APPROVER")));
        Assert.Null(Approve(1, Roles(HrManagementRoles.BranchManager, HrManagementRoles.HrCore)));
        Assert.NotNull(Approve(1, Roles(HrManagementRoles.BranchManager)));
    }

    [Fact]
    public void Admin_passes_only_with_admin_bypass()
    {
        Assert.Equal(WorkflowStageGuard.StepRoleMessage(HrManagementRoles.HrManagementApprover),
            Approve(2, Roles(HrManagementRoles.Admin)));
        Assert.Null(Approve(2, Roles(HrManagementRoles.Admin), new WorkflowStepRoleOptions { AdminBypass = true }));
        Assert.NotNull(Approve(2, Roles(HrManagementRoles.HrCore), new WorkflowStepRoleOptions { AdminBypass = true }));
    }

    [Fact]
    public void Non_enforced_entity_types_and_an_empty_list_skip_the_check()
    {
        Assert.Null(Approve(2, Roles(HrManagementRoles.HrCore), entityType: "TransferRequest"));
        Assert.Null(Approve(2, Roles(HrManagementRoles.HrCore), new WorkflowStepRoleOptions { EnforceStepRoleEntityTypes = [] }));
        Assert.NotNull(Approve(2, Roles(HrManagementRoles.HrCore),
            new WorkflowStepRoleOptions { EnforceStepRoleEntityTypes = ["recruitmentrequisition"] }));
    }

    [Theory]
    [InlineData("RecruitmentRequisition")]
    [InlineData("OfferLetter")]
    [InlineData("SelectionCommitteeSession")]
    [InlineData("RecruitmentAppeal")]
    [InlineData("RecruitmentCompetition")]
    [InlineData("CompetitionResultVersion")]
    [InlineData("EstablishmentChangeRequest")]
    public void Recruitment_and_establishment_types_are_enforced_by_default(string entityType) =>
        Assert.True(new WorkflowStepRoleOptions().IsEnforced(entityType));

    [Theory]
    [InlineData("AssetAssignment")]
    [InlineData("AssetLossReport")]
    [InlineData("TransferRequest")]
    public void Other_domains_are_not_enforced_by_default(string entityType) =>
        Assert.False(new WorkflowStepRoleOptions().IsEnforced(entityType));

    [Fact]
    public void Reject_checks_status_step_and_role()
    {
        var options = new WorkflowStepRoleOptions();
        Assert.Null(WorkflowStageGuard.ValidateReject(State(2), Definition, Enforced, Roles(HrManagementRoles.HrManagementApprover), options));
        Assert.Equal(WorkflowStageGuard.StepRoleMessage(HrManagementRoles.HrManagementApprover),
            WorkflowStageGuard.ValidateReject(State(2), Definition, Enforced, Roles(HrManagementRoles.HrCore), options));
        Assert.Equal(WorkflowStageGuard.NotInProgressMessage,
            WorkflowStageGuard.ValidateReject(State(2, WorkflowStatus.Completed), Definition, Enforced, Roles(HrManagementRoles.HrManagementApprover), options));
        Assert.Equal(WorkflowStageGuard.StepNotFoundMessage(9),
            WorkflowStageGuard.ValidateReject(State(9), Definition, Enforced, Roles(HrManagementRoles.HrCore), options));
    }

    [Fact]
    public void Role_is_checked_before_the_initiator_rule_and_the_initiator_rule_still_holds()
    {
        var initiator = Guid.NewGuid();
        Assert.Equal(WorkflowStageGuard.StepRoleMessage(HrManagementRoles.HrManagementApprover),
            Approve(2, Roles(HrManagementRoles.HrCore), actor: initiator, initiator: initiator));
        Assert.Equal(WorkflowStageGuard.InitiatorLaterStageMessage,
            Approve(2, Roles(HrManagementRoles.HrManagementApprover), actor: initiator, initiator: initiator));
    }
}
