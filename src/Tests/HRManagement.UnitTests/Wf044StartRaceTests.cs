using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using static HRManagement.UnitTests.Wf023WorkInboxUiTests;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-44 (G-WF-12): what the coordinator does with the loser of a start race. <c>Wf044InstanceIntegrityPostgresTests</c> proves the race
/// on the real index; here the starter reports it, so each answer can be forced.
/// </summary>
public sealed class Wf044StartRaceTests
{
    private const string Code = "RECRUITMENT-REQUISITION-APPROVAL";
    private static readonly Guid Submitter = Guid.Parse("44000000-0000-4000-8000-0000000000b1");

    /// <summary>The winner commits between the loser's checks and its insert, which is the race the index closes.</summary>
    private static ApprovalStartCoordinator Coordinator(HRCoreDbContext db, Guid entityId, Func<Task> winnerCommits)
    {
        var starter = new Mock<IWorkflowStarter>();
        starter
            .Setup(s => s.StartByWorkflowCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await winnerCommits();
                throw new WorkflowInstanceRunningException("RecruitmentRequisition", entityId);
#pragma warning disable CS0162
                return (Guid?)null;
#pragma warning restore CS0162
            });
        return new ApprovalStartCoordinator(
            db, Mock.Of<IPolicyOrchestrator>(), starter.Object, new ConfigurationBuilder().Build());
    }

    private static ApprovalStartRequest Request(Guid entityId, Guid? actor) =>
        new("RecruitmentRequisition", entityId, null, Code, "core.recruitment", actor, "مقدم", null);

    private static async Task<WorkflowInstance> WinnerAsync(HRCoreDbContext db, Guid entityId, Guid? initiatedBy, string history = "[]")
    {
        var definition = new WorkflowDefinition
        {
            Code = Code,
            Name = "اعتماد",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "RecruitmentRequisition",
            StepsJson = "[]",
            GuardrailRolesJson = "[]"
        };
        db.WorkflowDefinitions.Add(definition);
        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = 1,
            EntityType = "RecruitmentRequisition",
            EntityId = entityId,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = initiatedBy,
            StepHistoryJson = history
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    [Fact]
    public async Task The_loser_of_a_double_submission_gets_the_winners_instance()
    {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        WorkflowInstance? winner = null;

        var result = await Coordinator(db, entityId, async () => winner = await WinnerAsync(db, entityId, Submitter))
            .StartAsync(Request(entityId, Submitter));

        Assert.Equal(winner!.Id, result.InstanceId);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Someone_elses_running_instance_is_not_adopted()
    {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();

        var result = await Coordinator(db, entityId, () => WinnerAsync(db, entityId, Guid.NewGuid()))
            .StartAsync(Request(entityId, Submitter));

        Assert.Null(result.InstanceId);
        Assert.Equal(WorkflowInstanceRunningException.RunningMessage, result.Error);
    }

    [Fact]
    public async Task A_running_instance_seen_before_the_insert_refuses_the_start_without_reaching_the_index()
    {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        await WinnerAsync(db, entityId, Guid.NewGuid());
        var reached = false;

        var result = await Coordinator(db, entityId, () =>
        {
            reached = true;
            return Task.CompletedTask;
        }).StartAsync(Request(entityId, Submitter));

        Assert.False(reached);
        Assert.Equal(WorkflowInstanceRunningException.RunningMessage, result.Error);
    }

    [Fact]
    public async Task An_instance_that_already_carries_a_decision_is_not_adopted()
    {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();

        var result = await Coordinator(db, entityId, () => WinnerAsync(db, entityId, Submitter, """[{"Level":1,"Status":1}]"""))
            .StartAsync(Request(entityId, Submitter));

        Assert.Null(result.InstanceId);
        Assert.Equal(WorkflowInstanceRunningException.RunningMessage, result.Error);
    }
}
