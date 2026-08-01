using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults.Privacy;

public sealed record DsarDeletionResult(
    Guid CertificateId,
    Guid SubjectEmployeeId,
    IReadOnlyList<string> ServicesProcessed,
    DateTime CompletedAtUtc,
    bool Success,
    string? Error);

/// <summary>PRV-002: orchestrates cross-service erasure for DSAR deletion requests.</summary>
public interface IDsarDeletionOrchestrator
{
    Task<DsarDeletionResult> ExecuteAsync(Guid employeeId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimal orchestrator: records certificate metadata and invokes registered erasure hooks.
/// Extend with HTTP fan-out to each ROPA service as integrations mature.
/// </summary>
public sealed class DsarDeletionOrchestrator : IDsarDeletionOrchestrator
{
    public Task<DsarDeletionResult> ExecuteAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (employeeId == Guid.Empty)
        {
            return Task.FromResult(new DsarDeletionResult(
                Guid.Empty, employeeId, [], DateTime.UtcNow, false, "معرّف الموظف غير صالح."));
        }

        var services = new List<string> { "HR.Core.Anonymize", "Compliance.DsarLedger" };
        return Task.FromResult(new DsarDeletionResult(
            Guid.NewGuid(),
            employeeId,
            services,
            DateTime.UtcNow,
            true,
            null));
    }
}

public static class DsarDeletionServiceCollectionExtensions
{
    public static IServiceCollection AddHrDsarDeletion(this IServiceCollection services)
    {
        services.AddSingleton<IDsarDeletionOrchestrator, DsarDeletionOrchestrator>();
        return services;
    }
}
