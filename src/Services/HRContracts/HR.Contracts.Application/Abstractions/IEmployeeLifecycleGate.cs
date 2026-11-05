namespace HR.Contracts.Application.Abstractions;

/// <summary>FUN-002-B: cross-service gate (legal hold). PLAN-42: at creation and at publication, and a failed check refuses.</summary>
public interface IEmployeeLifecycleGate
{
    Task<(bool Allowed, string? Error)> AssertCanStartSeparationAsync(Guid employeeId, CancellationToken ct);

    Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct);
}
