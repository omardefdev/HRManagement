using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Enums;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.TestSupport.Workflow;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests.Support;

/// <summary>
/// PLAN-60 (P-13): an integration test reaches a separation's status the way the product does — the one creation path, a submission to
/// PLAN-46's fake engine, the engine's outcome applied as the consumer applies it, the numbered decision, the employee's notification —
/// never by writing a status. Each call opens its own scope of <paramref name="services"/>, as each request does.
/// </summary>
public static class SeparationJourneyDriver
{
    public static readonly Guid Maker = Guid.Parse("60d00000-0000-4000-8000-0000000000a1");
    public static readonly Guid Reviewer = Guid.Parse("60d00000-0000-4000-8000-0000000000a2");
    public static readonly Guid Authority = Guid.Parse("60d00000-0000-4000-8000-0000000000a3");
    public static readonly Guid Checker = Guid.Parse("60d00000-0000-4000-8000-0000000000a4");

    private static async Task<T> InScopeAsync<T>(IServiceProvider services, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public static Task<Guid> CreateAsync(
        IServiceProvider services, Guid employeeId, Guid? contractId, DateOnly effectiveDate, SeparationType type = SeparationType.Resignation) =>
        InScopeAsync(services, async sp =>
        {
            var created = await sp.GetRequiredService<IMediator>().Send(new CreateSeparationRecordCommand(
                new SeparationRecordDto(null, employeeId, contractId, type, effectiveDate, "اختبار تكامل", null)));
            Assert.True(created.Success, created.Error);
            return created.Record!.Id;
        });

    /// <summary>Submitted by the maker and approved by the engine (both steps), applied as the consumer applies it.</summary>
    public static Task ApproveAsync(IServiceProvider services, Guid id) =>
        InScopeAsync(services, async sp =>
        {
            var submitted = await sp.GetRequiredService<IMediator>().Send(new SubmitSeparationCommand(id, Maker));
            Assert.True(submitted.Success, submitted.Error);
            await sp.GetRequiredService<SeparationApprovalService>().ApplyOutcomeAsync(FakeWorkflowApprovalClient.Outcome(
                "SeparationRecord", id, submitted.InstanceId!.Value, WorkflowApprovalOutcomes.Approved, null, Authority,
                decisions:
                [
                    new WorkflowStepDecisionDto(1, "hr-core", Reviewer, "Approved", DateTime.UtcNow),
                    new WorkflowStepDecisionDto(2, "hr-management-approver", Authority, "Approved", DateTime.UtcNow)
                ]));
            return 0;
        });

    public static Task<string> IssueDecisionAsync(IServiceProvider services, Guid id) =>
        InScopeAsync(services, async sp =>
        {
            var issued = await sp.GetRequiredService<IMediator>().Send(new IssueSeparationDecisionCommand(id, Checker, "المدقق"));
            Assert.True(issued.Success, issued.Error);
            return issued.Decision!.DecisionNumber;
        });

    public static Task NotifyAsync(IServiceProvider services, Guid id) =>
        InScopeAsync(services, async sp =>
        {
            var done = await sp.GetRequiredService<IMediator>().Send(new CompleteManualStepCommand(
                id, SeparationStep.EmployeeNotified, "notice:integration", null, null, Maker, null));
            Assert.True(done.Success, done.Error);
            return 0;
        });

    /// <summary>Created, approved, with its decision and its notification: what an execution needs.</summary>
    public static async Task<Guid> ReadyToExecuteAsync(
        IServiceProvider services, Guid employeeId, Guid? contractId, DateOnly effectiveDate, bool notify = true)
    {
        var id = await CreateAsync(services, employeeId, contractId, effectiveDate);
        await ApproveAsync(services, id);
        await IssueDecisionAsync(services, id);
        if (notify)
            await NotifyAsync(services, id);
        return id;
    }

    public static Task<(bool Success, string? Error)> ExecuteAsync(IServiceProvider services, Guid id) =>
        InScopeAsync(services, sp => sp.GetRequiredService<IMediator>().Send(new ExecuteSeparationCommand(id, Checker)));
}
