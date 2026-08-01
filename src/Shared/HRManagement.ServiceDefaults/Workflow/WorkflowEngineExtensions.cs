using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Workflow;

public static class WorkflowEngineExtensions
{
    /// <summary>Registers the configurable workflow engine (WF-001) in the DI container.</summary>
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
        return services;
    }
}
