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
}

public sealed record KeycloakProvisionResult(bool Attempted, bool Success, string? Detail)
{
    public static KeycloakProvisionResult Disabled() => new(false, true, "sync disabled");
    public static KeycloakProvisionResult Succeeded(string? id) => new(true, true, id);
    public static KeycloakProvisionResult Failed(string error) => new(true, false, error);
}
