using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Workflow;

namespace HRManagement.UnitTests;

/// <summary>WF-001: Configurable Workflow Engine unit tests.</summary>
public class Wf001WorkflowEngineTests
{
    private readonly WorkflowEngine _engine = new();

    private static WorkflowDefinitionSnapshot CreateThreeStepDefinition() => new()
    {
        Id = Guid.NewGuid(),
        Code = "LEAVE-APPROVAL",
        Name = "Leave Approval",
        Version = 1,
        DomainEntityType = "LeaveRequest",
        Steps =
        [
            new WorkflowStepSnapshot { Level = 1, Name = "Manager Approval", StepType = WorkflowStepType.Approval, RoleRequired = "manager" },
            new WorkflowStepSnapshot { Level = 2, Name = "HR Review", StepType = WorkflowStepType.Approval, RoleRequired = "hr-core" },
            new WorkflowStepSnapshot { Level = 3, Name = "Director Sign-off", StepType = WorkflowStepType.Approval, RoleRequired = "director" }
        ],
        GuardrailRoles = ["hr-core"]
    };

    [Fact]
    public void Start_WithValidDefinition_ReturnsInProgress()
    {
        var def = CreateThreeStepDefinition();

        var result = _engine.Start(def, "LeaveRequest", Guid.NewGuid(), Guid.NewGuid(), "User1");

        Assert.True(result.Success);
        Assert.Equal(WorkflowStatus.InProgress, result.NewStatus);
        Assert.Equal(1, result.NewStepLevel);
    }

    [Fact]
    public void Start_WithEmptySteps_ReturnsFail()
    {
        var def = new WorkflowDefinitionSnapshot
        {
            Id = Guid.NewGuid(),
            Code = "EMPTY",
            Name = "Empty",
            Version = 1,
            DomainEntityType = "Test",
            Steps = []
        };

        var result = _engine.Start(def, "Test", Guid.NewGuid(), Guid.NewGuid(), "User1");

        Assert.False(result.Success);
        Assert.Contains("no steps", result.Error);
    }

    [Fact]
    public void Sequential_ApproveAllSteps_CompletesWorkflow()
    {
        var def = CreateThreeStepDefinition();
        var state = new WorkflowInstanceState
        {
            Id = Guid.NewGuid(),
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            StepHistory = []
        };

        var actor1 = Guid.NewGuid();
        var result1 = _engine.ApproveStep(state, def, actor1, "Manager1", "Approved by manager");
        Assert.True(result1.Success);
        Assert.Equal(WorkflowStatus.InProgress, result1.NewStatus);
        Assert.Equal(2, result1.NewStepLevel);

        state.CurrentStepLevel = result1.NewStepLevel;
        if (result1.HistoryEntry is not null) state.StepHistory.Add(result1.HistoryEntry);

        var actor2 = Guid.NewGuid();
        var result2 = _engine.ApproveStep(state, def, actor2, "HR Officer", "HR approved");
        Assert.True(result2.Success);
        Assert.Equal(WorkflowStatus.InProgress, result2.NewStatus);
        Assert.Equal(3, result2.NewStepLevel);

        state.CurrentStepLevel = result2.NewStepLevel;
        if (result2.HistoryEntry is not null) state.StepHistory.Add(result2.HistoryEntry);

        var actor3 = Guid.NewGuid();
        var result3 = _engine.ApproveStep(state, def, actor3, "Director", "Final sign-off");
        Assert.True(result3.Success);
        Assert.Equal(WorkflowStatus.Completed, result3.NewStatus);
    }

    [Fact]
    public void Reject_AtAnyStep_SetsStatusRejected()
    {
        var state = new WorkflowInstanceState
        {
            Id = Guid.NewGuid(),
            CurrentStepLevel = 2,
            Status = WorkflowStatus.InProgress,
            StepHistory = []
        };

        var result = _engine.Reject(state, Guid.NewGuid(), "Reviewer", "Not compliant");

        Assert.True(result.Success);
        Assert.Equal(WorkflowStatus.Rejected, result.NewStatus);
        Assert.NotNull(result.HistoryEntry);
        Assert.Equal(WorkflowStepStatus.Rejected, result.HistoryEntry!.Status);
    }

    [Fact]
    public void Reject_WhenNotInProgress_Fails()
    {
        var state = new WorkflowInstanceState
        {
            Id = Guid.NewGuid(),
            CurrentStepLevel = 1,
            Status = WorkflowStatus.Completed,
            StepHistory = []
        };

        var result = _engine.Reject(state, Guid.NewGuid(), "Reviewer", "Too late");

        Assert.False(result.Success);
        Assert.Contains("not in progress", result.Error);
    }

    [Fact]
    public void VersionPinning_InstanceRetainsOriginalVersion()
    {
        var defV1 = new WorkflowDefinitionSnapshot
        {
            Id = Guid.NewGuid(),
            Code = "TRANSFER",
            Name = "Transfer v1",
            Version = 1,
            DomainEntityType = "Transfer",
            Steps =
            [
                new WorkflowStepSnapshot { Level = 1, Name = "Manager", RoleRequired = "manager" },
                new WorkflowStepSnapshot { Level = 2, Name = "HR", RoleRequired = "hr-core" }
            ]
        };

        var startResult = _engine.Start(defV1, "Transfer", Guid.NewGuid(), Guid.NewGuid(), "Initiator");
        Assert.True(startResult.Success);

        var state = new WorkflowInstanceState
        {
            Id = Guid.NewGuid(),
            CurrentStepLevel = startResult.NewStepLevel,
            Status = startResult.NewStatus,
            StepHistory = []
        };

        var defV2 = defV1 with
        {
            Version = 2,
            Steps =
            [
                new WorkflowStepSnapshot { Level = 1, Name = "Manager", RoleRequired = "manager" },
                new WorkflowStepSnapshot { Level = 2, Name = "HR", RoleRequired = "hr-core" },
                new WorkflowStepSnapshot { Level = 3, Name = "CEO", RoleRequired = "ceo" }
            ]
        };

        // Instance continues with V1 definition (2 steps), not V2 (3 steps)
        var approve1 = _engine.ApproveStep(state, defV1, Guid.NewGuid(), "Manager", null);
        Assert.True(approve1.Success);
        Assert.Equal(2, approve1.NewStepLevel);

        state.CurrentStepLevel = approve1.NewStepLevel;
        if (approve1.HistoryEntry is not null) state.StepHistory.Add(approve1.HistoryEntry);

        var approve2 = _engine.ApproveStep(state, defV1, Guid.NewGuid(), "HR", null);
        Assert.True(approve2.Success);
        Assert.Equal(WorkflowStatus.Completed, approve2.NewStatus);

        // With V2 it would still be InProgress (needs CEO step)
        state.CurrentStepLevel = 2;
        state.Status = WorkflowStatus.InProgress;
        var approve2v2 = _engine.ApproveStep(state, defV2, Guid.NewGuid(), "HR", null);
        Assert.True(approve2v2.Success);
        Assert.Equal(WorkflowStatus.InProgress, approve2v2.NewStatus);
        Assert.Equal(3, approve2v2.NewStepLevel);
    }

    [Fact]
    public void Condition_StepSkipped_WhenConditionFails()
    {
        var def = new WorkflowDefinitionSnapshot
        {
            Id = Guid.NewGuid(),
            Code = "FINANCIAL",
            Name = "Financial Approval",
            Version = 1,
            DomainEntityType = "Expense",
            Steps =
            [
                new WorkflowStepSnapshot { Level = 1, Name = "Manager", RoleRequired = "manager" },
                new WorkflowStepSnapshot
                {
                    Level = 2,
                    Name = "Finance Director",
                    RoleRequired = "finance-director",
                    Condition = new WorkflowCondition { Field = "Amount", Operator = "GreaterThan", Value = "10000" }
                },
                new WorkflowStepSnapshot { Level = 3, Name = "HR", RoleRequired = "hr-core" }
            ]
        };

        // Context with Amount = 5000 → step 2 should be skipped
        var context = new Dictionary<string, string> { ["Amount"] = "5000" };
        var state = new WorkflowInstanceState
        {
            Id = Guid.NewGuid(),
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            StepHistory = [],
            ContextJson = """{"Amount":"5000"}"""
        };

        var result = _engine.ApproveStep(state, def, Guid.NewGuid(), "Manager", "OK", context);

        Assert.True(result.Success);
        Assert.Equal(WorkflowStatus.InProgress, result.NewStatus);
        Assert.Equal(3, result.NewStepLevel); // Skipped level 2
    }

    [Fact]
    public void Condition_StepExecuted_WhenConditionPasses()
    {
        var def = new WorkflowDefinitionSnapshot
        {
            Id = Guid.NewGuid(),
            Code = "FINANCIAL",
            Name = "Financial Approval",
            Version = 1,
            DomainEntityType = "Expense",
            Steps =
            [
                new WorkflowStepSnapshot { Level = 1, Name = "Manager", RoleRequired = "manager" },
                new WorkflowStepSnapshot
                {
                    Level = 2,
                    Name = "Finance Director",
                    RoleRequired = "finance-director",
                    Condition = new WorkflowCondition { Field = "Amount", Operator = "GreaterThan", Value = "10000" }
                },
                new WorkflowStepSnapshot { Level = 3, Name = "HR", RoleRequired = "hr-core" }
            ]
        };

        // Context with Amount = 50000 → step 2 should execute
        var context = new Dictionary<string, string> { ["Amount"] = "50000" };
        var state = new WorkflowInstanceState
        {
            Id = Guid.NewGuid(),
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            StepHistory = [],
            ContextJson = """{"Amount":"50000"}"""
        };

        var result = _engine.ApproveStep(state, def, Guid.NewGuid(), "Manager", "OK", context);

        Assert.True(result.Success);
        Assert.Equal(WorkflowStatus.InProgress, result.NewStatus);
        Assert.Equal(2, result.NewStepLevel); // Goes to level 2 (condition passes)
    }

    [Fact]
    public void WorkflowCondition_Evaluate_Operators()
    {
        var ctx = new Dictionary<string, string>
        {
            ["Status"] = "Active",
            ["Amount"] = "15000",
            ["Department"] = "Engineering"
        };

        Assert.True(new WorkflowCondition { Field = "Status", Operator = "Equals", Value = "Active" }.Evaluate(ctx));
        Assert.False(new WorkflowCondition { Field = "Status", Operator = "Equals", Value = "Inactive" }.Evaluate(ctx));
        Assert.True(new WorkflowCondition { Field = "Status", Operator = "NotEquals", Value = "Inactive" }.Evaluate(ctx));
        Assert.True(new WorkflowCondition { Field = "Amount", Operator = "GreaterThan", Value = "10000" }.Evaluate(ctx));
        Assert.False(new WorkflowCondition { Field = "Amount", Operator = "LessThan", Value = "10000" }.Evaluate(ctx));
        Assert.True(new WorkflowCondition { Field = "Department", Operator = "Contains", Value = "Engineer" }.Evaluate(ctx));
        Assert.True(new WorkflowCondition { Field = "Department", Operator = "In", Value = "HR,Engineering,Finance" }.Evaluate(ctx));
        Assert.False(new WorkflowCondition { Field = "Department", Operator = "In", Value = "HR,Finance" }.Evaluate(ctx));
    }

    [Fact]
    public void WorkflowCondition_Evaluate_MissingField_ReturnsFalse()
    {
        var ctx = new Dictionary<string, string> { ["Name"] = "Test" };
        var condition = new WorkflowCondition { Field = "Missing", Operator = "Equals", Value = "anything" };

        Assert.False(condition.Evaluate(ctx));
    }

    [Fact]
    public void WorkflowCondition_ParseContext_ValidJson()
    {
        var json = """{"Amount":"5000","Status":"Active"}""";
        var ctx = WorkflowCondition.ParseContext(json);

        Assert.NotNull(ctx);
        Assert.Equal("5000", ctx!["Amount"]);
        Assert.Equal("Active", ctx["Status"]);
    }

    [Fact]
    public void WorkflowCondition_ParseContext_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(WorkflowCondition.ParseContext(null));
        Assert.Null(WorkflowCondition.ParseContext(""));
        Assert.Null(WorkflowCondition.ParseContext("  "));
    }

    [Fact]
    public void Bridge_CreateViaEngineOrLegacy_WithDefinition_CreatesBoth()
    {
        var def = new WorkflowDefinitionSnapshot
        {
            Id = Guid.NewGuid(),
            Code = "TEST",
            Name = "Test",
            Version = 1,
            DomainEntityType = "TestEntity",
            Steps = [new WorkflowStepSnapshot { Level = 1, Name = "Step1", RoleRequired = "admin" }]
        };

        var (request, instance) = HR.Core.Domain.Services.ApprovalWorkflowService.CreateViaEngineOrLegacy(
            "TestEntity", Guid.NewGuid(), "Test desc", Guid.NewGuid(), "User",
            workflowDefinition: def);

        Assert.NotNull(request);
        Assert.NotNull(instance);
        Assert.Equal(def.Id, instance!.DefinitionId);
        Assert.Equal(def.Version, instance.DefinitionVersion);
        Assert.Equal(request.Id, instance.ApprovalRequestId);
    }

    [Fact]
    public void Bridge_CreateViaEngineOrLegacy_WithoutDefinition_LegacyOnly()
    {
        var (request, instance) = HR.Core.Domain.Services.ApprovalWorkflowService.CreateViaEngineOrLegacy(
            "TestEntity", Guid.NewGuid(), "Test desc", Guid.NewGuid(), "User",
            workflowDefinition: null);

        Assert.NotNull(request);
        Assert.Null(instance);
    }
}
