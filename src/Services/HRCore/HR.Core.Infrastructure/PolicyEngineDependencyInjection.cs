using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.ServiceDefaults.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HR.Core.Infrastructure;

public static class PolicyEngineDependencyInjection
{
    public static IServiceCollection AddHrPolicyEngine(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IPolicyPublishedSnapshotCache, PolicyPublishedSnapshotCache>();
        services.AddScoped<IPolicyVersionPublisher, PolicyVersionPublisher>();
        services.AddScoped<IPolicyOrchestrator, PolicyOrchestrator>();
        services.AddScoped<IWorkflowSignatureService, WorkflowSignatureService>();
        services.AddScoped<IWorkflowStarter, WorkflowStarter>();
        services.AddScoped<ApprovalStartCoordinator>();
        // PLAN-25 (G-04): one gate per entity type; the evaluator (guard + gate) is shared by the server and the panels.
        services.AddScoped<WorkflowGateOrgScope>();
        services.AddScoped<IWorkflowApprovalGate, RequisitionApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, OfferApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, CommitteeApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, AppealApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, CompetitionApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, ResultsApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, EstablishmentChangeApprovalGate>();
        services.TryAddSingleton(new WorkflowStepRoleOptions());
        services.AddScoped<WorkflowApprovalEvaluator>();
        services.AddScoped<IWorkflowInstanceActionService, WorkflowInstanceActionService>();
        return services;
    }
}
