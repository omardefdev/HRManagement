using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Workflow;

namespace HRManagement.UnitTests;

/// <summary>PLAN-03 W3.2: stage-skip and initiator checker guards.</summary>
public class WorkflowStageGuardTests
{
    private static WorkflowDefinitionSnapshot TwoStepDefinition() => new()
    {
        Id = Guid.NewGuid(),
        Code = "ASSET-ASSIGN-APPROVAL",
        Name = "Asset",
        Version = 2,
        DomainEntityType = "AssetAssignment",
        Steps =
        [
            new WorkflowStepSnapshot { Level = 1, Name = "Logistics", StepType = WorkflowStepType.Approval, RoleRequired = "hr-core" },
            new WorkflowStepSnapshot { Level = 2, Name = "Management", StepType = WorkflowStepType.Approval, RoleRequired = "hr-management-approver" }
        ]
    };

    [Fact]
    public void Rejects_target_step_not_equal_to_current()
    {
        var state = new WorkflowInstanceState { CurrentStepLevel = 1, Status = WorkflowStatus.InProgress, StepHistory = [] };
        var error = WorkflowStageGuard.ValidateApprove(state, TwoStepDefinition(), Guid.NewGuid(), Guid.NewGuid(), targetStepLevel: 2);
        Assert.Contains("skip", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_initiator_on_checker_stage()
    {
        var initiator = Guid.NewGuid();
        var state = new WorkflowInstanceState { CurrentStepLevel = 2, Status = WorkflowStatus.InProgress, StepHistory = [] };
        var error = WorkflowStageGuard.ValidateApprove(state, TwoStepDefinition(), initiator, initiator, targetStepLevel: null);
        Assert.Contains("initiator", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Engine_rejects_same_actor_twice_on_same_step()
    {
        var engine = new WorkflowEngine();
        var def = TwoStepDefinition();
        var actor = Guid.NewGuid();
        var state = new WorkflowInstanceState
        {
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            StepHistory =
            [
                new WorkflowStepHistoryEntry
                {
                    Level = 1,
                    Status = WorkflowStepStatus.Approved,
                    ActorId = actor,
                    ActionAt = DateTime.UtcNow
                }
            ]
        };

        var result = engine.ApproveStep(state, def, actor, "Same", "again");
        Assert.False(result.Success);
        Assert.Contains("Same actor", result.Error);
    }
}
