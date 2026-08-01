namespace HR.Contracts.Application.Abstractions;

/// <summary>FUN-002-B: cross-service gate (legal hold) before publishing separation saga.</summary>
public interface IEmployeeLifecycleGate
{
    Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct);
}
