using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IWorkflowInstanceActionService, WorkflowInstanceActionService>();
        return services;
    }
}
