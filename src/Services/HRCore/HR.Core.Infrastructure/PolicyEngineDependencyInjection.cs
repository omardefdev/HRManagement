using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HR.Core.Infrastructure;

public static class PolicyEngineDependencyInjection
{
    public static IServiceCollection AddHrPolicyEngine(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHrPolicyEngineConfiguration();
        services.AddSingleton<IPolicyPublishedSnapshotCache, PolicyPublishedSnapshotCache>();
        services.AddScoped<IPolicyVersionPublisher, PolicyVersionPublisher>();
        // PLAN-49 (G-POL-02): what a domain may do comes from the domain catalog; PLAN-47's allow-all default is gone.
        services.TryAddSingleton<HR.Core.Domain.Services.Policy.IPolicyDomainCapabilities>(CatalogPolicyDomainCapabilities.Instance);
        services.AddScoped<IPolicyRuleValidationService, PolicyRuleValidationService>();
        services.AddScoped<PolicyDraftService>();
        services.AddScoped<PolicyPublishService>();
        services.AddScoped<IPolicyOrchestrator, PolicyOrchestrator>();
        services.AddScoped<IWorkflowSignatureService, WorkflowSignatureService>();
        services.AddScoped<IWorkflowStarter, WorkflowStarter>();
        services.AddScoped<ApprovalStartCoordinator>();
        services.AddScoped<WorkflowDefinitionLifecycleService>();
        services.AddScoped<HR.Core.Infrastructure.Services.Workflow.External.ExternalApprovalService>();
        // PLAN-25 (G-04): one gate per entity type; the evaluator (guard + gate) is shared by the server and the panels.
        services.AddScoped<WorkflowGateOrgScope>();
        services.AddScoped<IWorkflowApprovalGate, RequisitionApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, OfferApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, CommitteeApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, AppealApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, CompetitionApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, ResultsApprovalGate>();
        services.AddScoped<IWorkflowApprovalGate, EstablishmentChangeApprovalGate>();
        foreach (var type in ExternalApprovalTypes.All)
            services.AddScoped<IWorkflowApprovalGate>(sp => new ExternalApprovalGate(type, sp.GetRequiredService<WorkflowGateOrgScope>()));
        // PLAN-50 (§5): HR.Core reads delegations from its own database, with no cache (a revocation acts at once).
        services.AddScoped<HR.Core.Infrastructure.Services.Delegation.DbApprovalDelegationLookup>();
        services.AddScoped<HRManagement.ServiceDefaults.Delegation.IApprovalDelegationLookup>(sp =>
            sp.GetRequiredService<HR.Core.Infrastructure.Services.Delegation.DbApprovalDelegationLookup>());
        services.TryAddSingleton(new WorkflowStepRoleOptions());
        services.AddScoped<WorkflowApprovalEvaluator>();
        services.AddScoped<IWorkflowInstanceActionService, WorkflowInstanceActionService>();
        return services;
    }
}
