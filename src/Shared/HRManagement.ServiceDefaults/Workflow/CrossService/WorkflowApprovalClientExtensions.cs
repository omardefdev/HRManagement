using HRManagement.ServiceDefaults.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Workflow.CrossService;

public static class WorkflowApprovalClientExtensions
{
    /// <summary>
    /// PLAN-46: the client of the contract. Unlike the other HR.Core clients a missing address does not stop the start-up: the service still
    /// serves its records, and every approval answers <see cref="WorkflowApprovalErrorCodes.EngineUnavailable"/> with an error in the log.
    /// </summary>
    public static IServiceCollection AddWorkflowApprovalClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceAuthenticatedClient(HttpWorkflowApprovalClient.HttpClientName, BaseUri(configuration), forwardActingUser: true);
        services.AddScoped<IWorkflowApprovalClient, HttpWorkflowApprovalClient>();
        return services;
    }

    /// <summary>PLAN-46: the owner-side sweep of one type. <typeparamref name="TStore"/> is scoped: a record gets a scope of its own.</summary>
    public static IServiceCollection AddWorkflowApprovalSweep<TStore>(this IServiceCollection services, IConfiguration configuration)
        where TStore : class, IWorkflowApprovalOwnerStore
    {
        services.Configure<WorkflowApprovalSweepOptions>(configuration.GetSection(WorkflowApprovalSweepOptions.SectionName));
        services.AddScoped<TStore>();
        services.AddHostedService<WorkflowApprovalSweepJob<TStore>>();
        return services;
    }

    private static Uri? BaseUri(IConfiguration configuration)
    {
        var address = configuration[HrCoreEndpoint.Key];
        return !string.IsNullOrWhiteSpace(address) && Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ? uri : null;
    }
}
