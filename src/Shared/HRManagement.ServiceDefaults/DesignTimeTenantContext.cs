using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Design-time / fallback tenant context. Bypasses filters so migrations and tooling can see all rows.
/// </summary>
public sealed class DesignTimeTenantContext : ITenantContext
{
    public static DesignTimeTenantContext Instance { get; } = new();

    private DesignTimeTenantContext()
    {
    }

    public Guid? TenantId => null;

    public bool IsPlatformOperator => true;

    public Guid RequireTenantId() =>
        throw new InvalidOperationException("Design-time tenant context has no tenant.");
}
