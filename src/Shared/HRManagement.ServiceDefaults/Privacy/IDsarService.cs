using HRManagement.SharedKernel.Privacy;

namespace HRManagement.ServiceDefaults.Privacy;

/// <summary>PRV-002: individual-rights (DSAR) operations — national contract.</summary>
public interface IDsarService
{
    Task<DsarRequest> SubmitAsync(DsarSubmitRequest request, CancellationToken cancellationToken = default);

    Task<DsarRequest?> GetAsync(Guid tenantId, Guid requestId, CancellationToken cancellationToken = default);

    Task<DsarRequest> TransitionAsync(
        Guid tenantId,
        Guid requestId,
        DsarRequestStatus newStatus,
        DsarTransitionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> DiscoverSourcesAsync(
        Guid tenantId,
        Guid subjectId,
        CancellationToken cancellationToken = default);
}

public sealed record DsarSubmitRequest(
    Guid TenantId,
    Guid SubjectId,
    DsarRequestType RequestType,
    string? SubjectNationalIdHash = null,
    string? IdentityProofMethod = null,
    string? Notes = null,
    Guid? AppealOfRequestId = null);

public sealed record DsarTransitionOptions(
    string? DiscoveryPackageJson = null,
    string? DiscoveryScopeJson = null,
    string? ExceptionCode = null,
    string? ExceptionReason = null,
    Guid? AssignedToUserId = null);

/// <summary>
/// In-memory stub for PRV-002 workflow validation and tests.
/// Production Compliance service replaces this with persisted implementation.
/// </summary>
public sealed class DsarService : IDsarService
{
    private readonly Dictionary<Guid, DsarRequest> _store = new();

    public Task<DsarRequest> SubmitAsync(DsarSubmitRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var entity = new DsarRequest
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            SubjectId = request.SubjectId,
            RequestType = request.RequestType,
            Status = DsarRequestStatus.Received,
            RequestedAtUtc = now,
            DeadlineUtc = DsarWorkflowRules.ComputeDeadlineUtc(request.RequestType, now),
            SubjectNationalIdHash = request.SubjectNationalIdHash,
            IdentityProofMethod = request.IdentityProofMethod,
            Notes = request.Notes,
            AppealOfRequestId = request.AppealOfRequestId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _store[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<DsarRequest?> GetAsync(Guid tenantId, Guid requestId, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(requestId, out var entity) && entity.TenantId == tenantId)
            return Task.FromResult<DsarRequest?>(entity);
        return Task.FromResult<DsarRequest?>(null);
    }

    public Task<DsarRequest> TransitionAsync(
        Guid tenantId,
        Guid requestId,
        DsarRequestStatus newStatus,
        DsarTransitionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!_store.TryGetValue(requestId, out var entity) || entity.TenantId != tenantId)
            throw new KeyNotFoundException($"DSAR {requestId} not found for tenant {tenantId}.");

        if (!DsarWorkflowRules.IsAllowedTransition(entity.Status, newStatus))
            throw new InvalidOperationException(
                $"Transition {entity.Status} → {newStatus} is not allowed (PRV-002).");

        entity.Status = newStatus;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        if (options is not null)
        {
            if (options.DiscoveryPackageJson is not null)
                entity.DiscoveryPackageJson = options.DiscoveryPackageJson;
            if (options.DiscoveryScopeJson is not null)
                entity.DiscoveryScopeJson = options.DiscoveryScopeJson;
            if (options.ExceptionCode is not null)
                entity.ExceptionCode = options.ExceptionCode;
            if (options.ExceptionReason is not null)
                entity.ExceptionReason = options.ExceptionReason;
            if (options.AssignedToUserId is not null)
                entity.AssignedToUserId = options.AssignedToUserId;
        }

        if (newStatus is DsarRequestStatus.Fulfilled
            or DsarRequestStatus.PartiallyFulfilled
            or DsarRequestStatus.Rejected
            or DsarRequestStatus.Closed)
            entity.CompletedAtUtc ??= DateTime.UtcNow;

        return Task.FromResult(entity);
    }

    public Task<IReadOnlyList<string>> DiscoverSourcesAsync(
        Guid tenantId,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        // Stub: national platform services that may hold subject data (PRV-001 ROPA alignment).
        IReadOnlyList<string> sources =
        [
            "HR.Core",
            "Payroll",
            "HR.Contracts",
            "HR.Movements",
            "HR.Health",
            "HR.Training",
            "File.Storage",
            "Records.Archive",
            "IDCard",
            "Compliance",
            "Dev.Analytics"
        ];
        return Task.FromResult(sources);
    }
}
