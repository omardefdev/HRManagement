namespace HRManagement.SharedKernel.Tenancy;

/// <summary>
/// Current tenant boundary for HTTP, messaging, and background work (TEN-001).
/// </summary>
public interface ITenantContext
{
    /// <summary>Resolved tenant id from JWT claim or <c>TenantIdFlow</c>; null when unset.</summary>
    Guid? TenantId { get; }

    /// <summary>True when the caller holds the platform-admin role (cross-tenant, audited).</summary>
    bool IsPlatformOperator { get; }

    /// <summary>Returns <see cref="TenantId"/> or throws when tenant context is required.</summary>
    Guid RequireTenantId();
}
