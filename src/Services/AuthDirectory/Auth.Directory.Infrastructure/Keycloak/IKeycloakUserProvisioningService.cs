namespace Auth.Directory.Infrastructure.Keycloak;

public interface IKeycloakUserProvisioningService
{
    /// <summary>Creates or reconciles a Keycloak user when directory sync is enabled.</summary>
    Task<KeycloakProvisionResult> TryEnsureUserAsync(
        string username,
        string email,
        string displayName,
        Guid employeeId,
        Guid? scopeOrgUnitId = null,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Disables a Keycloak user account.</summary>
    Task<KeycloakProvisionResult> TryDisableUserAsync(string keycloakUserId, CancellationToken ct = default);

    /// <summary>Enables a Keycloak user account.</summary>
    Task<KeycloakProvisionResult> TryEnableUserAsync(string keycloakUserId, CancellationToken ct = default);

    /// <summary>Assigns realm-level roles to a Keycloak user.</summary>
    Task<KeycloakProvisionResult> TryAssignRealmRolesAsync(string keycloakUserId, string[] roleNames, CancellationToken ct = default);

    /// <summary>Synchronises display name and email on the Keycloak user.</summary>
    Task<KeycloakProvisionResult> TrySyncUserInfoAsync(string keycloakUserId, string displayName, string email, CancellationToken ct = default);

    /// <summary>PLAN-52 (D-52-4): removes the <c>scope_org_unit_id</c> attribute of a transferred restricted user; his other attributes stay.</summary>
    Task<KeycloakProvisionResult> TryClearScopeAttributeAsync(string keycloakUserId, CancellationToken ct = default);
}

public sealed record KeycloakProvisionResult(bool Attempted, bool Success, string? Detail)
{
    public static KeycloakProvisionResult Disabled() => new(false, true, "sync disabled");
    public static KeycloakProvisionResult Succeeded(string? id) => new(true, true, id);

    /// <summary>PLAN-52: <see cref="IKeycloakUserProvisioningService.TryClearScopeAttributeAsync"/> removed the attribute (the user had one).</summary>
    public const string ScopeRemoved = "scope removed";

    /// <summary>PLAN-52: the user had no scope attribute; nothing was written.</summary>
    public const string ScopeAbsent = "no scope";

    public bool RemovedScope => Success && Detail == ScopeRemoved;
    public static KeycloakProvisionResult Failed(string error) => new(true, false, error);
}
