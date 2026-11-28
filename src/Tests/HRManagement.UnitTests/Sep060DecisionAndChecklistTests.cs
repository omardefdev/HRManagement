using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Consumers;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Sagas;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.TestSupport.Workflow;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>Moves a harness record to Approved through the engine's own outcome.</summary>
internal static class Sep060Approved
{
    public static async Task<SeparationRecord> ApprovedAsync(Sep060ApprovalHarness h, Guid creator, Guid? employee = null)
    {
        var record = await h.DraftAsync(creator, employee: employee);
        var instance = (await h.SubmitAsync(record.Id, creator)).InstanceId!.Value;
        var authority = Guid.NewGuid();
        await h.ApplyAsync(FakeWorkflowApprovalClient.Outcome("SeparationRecord", record.Id, instance, WorkflowApprovalOutcomes.Approved,
            Sep060ApprovalHarness.Tenant, authority, decisions: [new WorkflowStepDecisionDto(2, "hr-management-approver", authority, "Approved", DateTime.UtcNow)]));
        return await h.ReadAsync(record.Id);
    }

    public static Task<T> SendAsync<T>(Sep060ApprovalHarness h, IRequest<T> request) =>
        h.InScopeAsync(sp => sp.GetRequiredService<IMediator>().Send(request));

    public static Task<SeparationChecklist> ItemAsync(Sep060ApprovalHarness h, Guid record, SeparationStep step) =>
        h.InScopeAsync(sp => sp.GetRequiredService<ContractsDbContext>().SeparationChecklists.AsNoTracking()
            .SingleAsync(c => c.SeparationRecordId == record && c.Step == step));
}

/// <summary>PLAN-60 (§3, P-8): the numbered separation decision.</summary>
public sealed class Sep060DecisionTests
{
    [Fact]
    public async Task An_approved_record_gets_one_DEC_SEP_decision_linked_with_its_item_completed_by_the_issuer()
    {
        await using var h = new Sep060ApprovalHarness();
        var creator = Guid.NewGuid();
        var issuer = Guid.NewGuid();
        var record = await Sep060Approved.ApprovedAsync(h, creator);

        var result = await Sep060Approved.SendAsync(h, new IssueSeparationDecisionCommand(record.Id, issuer, "مدير الموارد"));

        Assert.True(result.Success, result.Error);
        Assert.Matches(@"^DEC-SEP-\d{4}-\d{5}$", result.Decision!.DecisionNumber);
        var request = Assert.Single(h.Codes.Requests);
        Assert.Equal(HRManagement.SharedKernel.CodeGeneration.CodeEntityKind.AdministrativeDecision, request.EntityKind);
        Assert.Equal("SEP", request.Facts["TypeCode"]);
        Assert.Equal("قرار إنهاء خدمة: سلمى الحسن — استقالة — نافذ 2026-12-31", result.Decision.Subject);
        Assert.Equal((record.EmployeeId, "مدير الموارد"), (result.Decision.RelatedEmployeeId!.Value, result.Decision.IssuedBy!));
        Assert.Equal(new DateOnly(2026, 11, 28), result.Decision.IssuedDate);

        Assert.Equal(result.Decision.Id, (await h.ReadAsync(record.Id)).DecisionId);
        var item = await Sep060Approved.ItemAsync(h, record.Id, SeparationStep.DecisionIssued);
        Assert.Equal((true, issuer, result.Decision.DecisionNumber), (item.IsCompleted, item.CompletedBy!.Value, item.EvidenceRef!));
    }

    [Fact]
    public async Task A_second_call_gives_back_the_same_decision_and_spends_no_number()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await Sep060Approved.ApprovedAsync(h, Guid.NewGuid());
        var first = await Sep060Approved.SendAsync(h, new IssueSeparationDecisionCommand(record.Id, Guid.NewGuid(), null));
        var second = await Sep060Approved.SendAsync(h, new IssueSeparationDecisionCommand(record.Id, Guid.NewGuid(), null));

        Assert.True(second.AlreadyIssued);
        Assert.Equal(first.Decision!.Id, second.Decision!.Id);
        Assert.Single(h.Codes.Requests);
    }

    [Fact]
    public async Task The_creator_never_issues_the_decision_while_the_rule_is_on()
    {
        await using var h = new Sep060ApprovalHarness();
        var creator = Guid.NewGuid();
        var record = await Sep060Approved.ApprovedAsync(h, creator);

        var refused = await Sep060Approved.SendAsync(h, new IssueSeparationDecisionCommand(record.Id, creator, null));

        Assert.Equal((false, IssueSeparationDecisionHandler.IssuerIsCreatorMessage), (refused.Success, refused.Error));
        Assert.Empty(h.Codes.Requests);
        Assert.Null((await h.ReadAsync(record.Id)).DecisionId);
    }

    [Fact]
    public async Task With_the_rule_off_the_creator_may_issue_it()
    {
        await using var h = new Sep060ApprovalHarness(new SeparationOptions { Decision = new SeparationDecisionOptions { IssuerMustDifferFromCreator = false } });
        var creator = Guid.NewGuid();
        var record = await Sep060Approved.ApprovedAsync(h, creator);
        Assert.True((await Sep060Approved.SendAsync(h, new IssueSeparationDecisionCommand(record.Id, creator, null))).Success);
    }

    [Theory]
    [InlineData(SeparationStatus.Draft)]
    [InlineData(SeparationStatus.PendingApproval)]
    [InlineData(SeparationStatus.Rejected)]
    public async Task A_record_that_is_not_approved_gets_no_decision(SeparationStatus status)
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        if (status != SeparationStatus.Draft)
        {
            var instance = (await h.SubmitAsync(record.Id, Guid.NewGuid())).InstanceId!.Value;
            if (status == SeparationStatus.Rejected)
                await h.ApplyAsync(FakeWorkflowApprovalClient.Outcome("SeparationRecord", record.Id, instance, WorkflowApprovalOutcomes.Rejected,
                    Sep060ApprovalHarness.Tenant, Guid.NewGuid(), "سبب"));
        }

        var refused = await Sep060Approved.SendAsync(h, new IssueSeparationDecisionCommand(record.Id, Guid.NewGuid(), null));
        Assert.Equal((false, IssueSeparationDecisionHandler.NotApprovedMessage), (refused.Success, refused.Error));
        Assert.Empty(h.Codes.Requests);
    }

    [Fact]
    public void The_decision_screens_refuse_a_linked_decision_on_edit_and_on_delete()
    {
        var source = System.IO.File.ReadAllText(Path.Combine(HRManagement.TestSupport.RepoPaths.Root,
            "src", "Services", "HRContracts", "HR.Contracts.Web", "Controllers", "AdministrativeDecisionsController.cs"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source,
            @"SeparationRecords\.AnyAsync\(r => r\.DecisionId == id\)").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, "LinkedToSeparationMessage;").Count);
    }
}

/// <summary>PLAN-60 (§4, P-9): the items by completion mode, the manual completion, the two-person exception, the parties' acknowledgements.</summary>
public sealed class Sep060ChecklistModeTests
{
    [Fact]
    public void Every_step_has_its_mode_and_every_party_ack_its_saga_party()
    {
        Assert.Equal(10, SeparationChecklistDefaults.DefaultSteps.Count);
        Assert.Equal(SeparationStepCompletionMode.System, SeparationChecklistDefaults.ModeFor(SeparationStep.DecisionIssued));
        Assert.Equal(SeparationStepCompletionMode.System, SeparationChecklistDefaults.ModeFor(SeparationStep.ContractTerminated));
        Assert.Equal(SeparationStepCompletionMode.Manual, SeparationChecklistDefaults.ModeFor(SeparationStep.EmployeeNotified));
        var parties = SeparationChecklistDefaults.DefaultSteps
            .Where(s => SeparationChecklistDefaults.ModeFor(s) == SeparationStepCompletionMode.PartyAck)
            .Select(SeparationChecklistDefaults.PartyFor)
            .ToList();
        // The seven PartyAck items are exactly the saga's mandatory parties, by ProcessParties' names.
        Assert.Equal(new SeparationSagaOptions().MandatoryParties.Order(), parties.Order()!);
        Assert.Contains(ProcessParties.ItInfra, parties);
        Assert.Contains(ProcessParties.HrCore, parties);
    }

    [Fact]
    public void Without_the_notification_rule_the_notification_is_not_required()
    {
        var items = SeparationChecklistDefaults.CreateForRecord(Guid.NewGuid(), requireEmployeeNotification: false).ToList();
        Assert.False(items.Single(i => i.Step == SeparationStep.EmployeeNotified).IsRequired);
        Assert.All(items.Where(i => i.Step != SeparationStep.EmployeeNotified), i => Assert.True(i.IsRequired));
    }

    [Theory]
    [InlineData(SeparationStep.PayrollSettled)]
    [InlineData(SeparationStep.CardRevoked)]
    [InlineData(SeparationStep.ItAccessRevoked)]
    [InlineData(SeparationStep.ContractTerminated)]
    [InlineData(SeparationStep.DecisionIssued)]
    public async Task No_party_ack_or_system_item_is_completed_by_hand(SeparationStep step)
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        var refused = await Sep060Approved.SendAsync(h, new CompleteManualStepCommand(record.Id, step, "دليل كافٍ", null, null, Guid.NewGuid(), null));
        Assert.Equal((false, SeparationChecklistRules.NotManualMessage), refused);
        Assert.False((await Sep060Approved.ItemAsync(h, record.Id, step)).IsCompleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcd")]
    public async Task A_manual_item_needs_evidence(string? evidence)
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        Assert.Equal((false, SeparationChecklistRules.EvidenceRequiredMessage),
            await Sep060Approved.SendAsync(h, new CompleteManualStepCommand(record.Id, SeparationStep.EmployeeNotified, evidence, null, null, Guid.NewGuid(), null)));

        // A file is evidence too.
        Assert.True((await Sep060Approved.SendAsync(h, new CompleteManualStepCommand(
            record.Id, SeparationStep.EmployeeNotified, null, Guid.NewGuid(), null, Guid.NewGuid(), null))).Success);
    }

    [Fact]
    public async Task A_manual_item_needs_the_signed_in_user()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        foreach (var actor in new Guid?[] { null, Guid.Empty })
            Assert.Equal((false, SeparationChecklistRules.NoActorMessage),
                await Sep060Approved.SendAsync(h, new CompleteManualStepCommand(record.Id, SeparationStep.EmployeeNotified, "دليل كافٍ", null, null, actor, null)));
    }

    [Fact]
    public async Task An_exception_takes_two_sessions_and_two_people_and_neither_the_requester_nor_the_creator_approves_it()
    {
        await using var h = new Sep060ApprovalHarness();
        var creator = Guid.NewGuid();
        var requester = Guid.NewGuid();
        var record = await Sep060Approved.ApprovedAsync(h, creator);

        // Twenty characters at least.
        Assert.Equal((false, SeparationChecklistRules.ReasonTooShortMessage), await Sep060Approved.SendAsync(h,
            new RequestStepExceptionCommand(record.Id, SeparationStep.EmployeeNotified, "قصير", requester)));
        // No approval without a request.
        Assert.Equal((false, SeparationChecklistRules.NoRequestMessage), await Sep060Approved.SendAsync(h,
            new ApproveStepExceptionCommand(record.Id, SeparationStep.EmployeeNotified, Guid.NewGuid())));

        Assert.True((await Sep060Approved.SendAsync(h, new RequestStepExceptionCommand(
            record.Id, SeparationStep.EmployeeNotified, "الموظف مسافر ولا يمكن تبليغه قبل النفاذ", requester))).Success);
        Assert.Equal((false, SeparationChecklistRules.SecondPersonMessage), await Sep060Approved.SendAsync(h,
            new ApproveStepExceptionCommand(record.Id, SeparationStep.EmployeeNotified, requester)));
        Assert.Equal((false, SeparationChecklistRules.SecondPersonMessage), await Sep060Approved.SendAsync(h,
            new ApproveStepExceptionCommand(record.Id, SeparationStep.EmployeeNotified, creator)));
        Assert.False((await Sep060Approved.ItemAsync(h, record.Id, SeparationStep.EmployeeNotified)).IsExcepted);

        var checker = Guid.NewGuid();
        Assert.True((await Sep060Approved.SendAsync(h, new ApproveStepExceptionCommand(record.Id, SeparationStep.EmployeeNotified, checker))).Success);
        var item = await Sep060Approved.ItemAsync(h, record.Id, SeparationStep.EmployeeNotified);
        Assert.True(item.IsExcepted);
        Assert.False(item.IsCompleted);
        Assert.Equal(checker, item.ExceptionApprovedBy);

        // The excepted item no longer holds the execution back.
        var items = await h.InScopeAsync(sp => sp.GetRequiredService<ContractsDbContext>().SeparationChecklists.AsNoTracking()
            .Where(c => c.SeparationRecordId == record.Id).ToListAsync());
        Assert.DoesNotContain(SeparationWorkflowRules.GetOpenPreExecutionSteps(items), s => s.Step == SeparationStep.EmployeeNotified);
    }

    [Fact]
    public async Task The_decision_is_never_excepted_and_a_party_item_is_not_excepted_from_HR_Contracts()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await h.DraftAsync(Guid.NewGuid());
        const string reason = "سبب طويل بما يكفي لطلب استثناء البند";
        Assert.Equal((false, SeparationChecklistRules.DecisionNeverExceptedMessage), await Sep060Approved.SendAsync(h,
            new RequestStepExceptionCommand(record.Id, SeparationStep.DecisionIssued, reason, Guid.NewGuid())));
        Assert.Equal((false, SeparationChecklistRules.PartyAckNotExceptedMessage), await Sep060Approved.SendAsync(h,
            new RequestStepExceptionCommand(record.Id, SeparationStep.AssetsReturned, reason, Guid.NewGuid())));
        Assert.Equal((false, SeparationChecklistRules.NotManualMessage), await Sep060Approved.SendAsync(h,
            new RequestStepExceptionCommand(record.Id, SeparationStep.ContractTerminated, reason, Guid.NewGuid())));
    }

    // ── The parties' acknowledgements (SeparationPartyAckChecklistConsumer).

    private static async Task<SeparationRecord> ExecutingAsync(Sep060ApprovalHarness h, string? sagaState)
    {
        var record = await Sep060Approved.ApprovedAsync(h, Guid.NewGuid());
        await h.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ContractsDbContext>();
            var tracked = await db.SeparationRecords.SingleAsync(r => r.Id == record.Id);
            SeparationWorkflowRules.Transition(tracked, SeparationStatus.Executing, Guid.NewGuid(), "تنفيذ");
            if (sagaState is not null)
                db.SeparationSagaStates.Add(new SeparationSagaState
                {
                    CorrelationId = record.Id,
                    SeparationRecordId = record.Id,
                    CurrentState = sagaState,
                    TenantId = Sep060ApprovalHarness.Tenant,
                    EmployeeId = record.EmployeeId,
                    SeparationType = "Resignation",
                    PendingParties = "",
                    CompletedParties = ""
                });
            await db.SaveChangesAsync();
        });
        return record;
    }

    private static Task AckAsync(Sep060ApprovalHarness h, Guid record, string party, string? evidence = "payroll:settlement:1") =>
        h.InScopeAsync(sp => new SeparationPartyAckChecklistConsumer(sp.GetRequiredService<ContractsDbContext>(),
                NullLogger<SeparationPartyAckChecklistConsumer>.Instance)
            .Consume(Mock.Of<ConsumeContext<ISeparationPartyCompleted>>(c =>
                c.Message == Mock.Of<ISeparationPartyCompleted>(m => m.SeparationRecordId == record && m.Party == party && m.EvidenceRef == evidence)
                && c.CancellationToken == CancellationToken.None)));

    [Theory]
    [InlineData(ProcessParties.Payroll, SeparationStep.PayrollSettled)]
    [InlineData(ProcessParties.ItInfra, SeparationStep.ItAccessRevoked)]
    [InlineData(ProcessParties.HrCore, SeparationStep.HrCoreUpdated)]
    [InlineData(ProcessParties.Archive, SeparationStep.Archived)]
    public async Task An_executions_acknowledgement_completes_its_partys_item_with_no_person(string party, SeparationStep step)
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await ExecutingAsync(h, "AwaitingParties");
        await AckAsync(h, record.Id, party);
        var item = await Sep060Approved.ItemAsync(h, record.Id, step);
        Assert.Equal((true, (Guid?)null, party, "payroll:settlement:1"), (item.IsCompleted, item.CompletedBy, item.SourceSystem!, item.EvidenceRef!));
    }

    [Theory]
    [InlineData("Compensating")]
    [InlineData("Cancelled")]
    public async Task A_compensations_acknowledgement_completes_nothing(string sagaState)
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await ExecutingAsync(h, sagaState);
        await AckAsync(h, record.Id, ProcessParties.Payroll);
        Assert.False((await Sep060Approved.ItemAsync(h, record.Id, SeparationStep.PayrollSettled)).IsCompleted);
    }

    [Fact]
    public async Task An_acknowledgement_before_the_execution_completes_nothing()
    {
        await using var h = new Sep060ApprovalHarness();
        var record = await Sep060Approved.ApprovedAsync(h, Guid.NewGuid());
        await AckAsync(h, record.Id, ProcessParties.Payroll);
        Assert.False((await Sep060Approved.ItemAsync(h, record.Id, SeparationStep.PayrollSettled)).IsCompleted);
    }
}
