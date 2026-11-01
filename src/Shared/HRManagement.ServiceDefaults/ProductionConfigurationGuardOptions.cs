namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-36 (G-OPS-01 blocker 3): which configuration keys must hold a real value before a service starts in Production.
/// PLAN-38 extends <see cref="CriticalKeys"/> to connection strings and broker credentials.
/// </summary>
public sealed class ProductionConfigurationGuardOptions
{
    /// <summary>Opt-in outside Production (Development, Acceptance): <c>Deployment:EnforceConfigGuard=true</c>.</summary>
    public const string EnforceKey = "Deployment:EnforceConfigGuard";

    /// <summary>Deployment mode; <c>demo</c> is the single-host demo profile (D-36-1).</summary>
    public const string DeploymentModeKey = "Deployment:Mode";

    public const int MinimumCallerProofSecretLength = 32;

    /// <summary>PLAN-38 (D-38-2): broker posture of the deployment, <c>least-privilege</c> or <c>admin</c>.</summary>
    public const string MqPostureKey = "Deployment:MqPosture";

    /// <summary>PLAN-38 (D-38-1): database posture of the deployment, <c>least-privilege</c> or <c>demo</c>.</summary>
    public const string DbPostureKey = "Deployment:DbPosture";

    public static readonly string[] MqPostures = ["least-privilege", "admin"];

    public static readonly string[] DbPostures = ["least-privilege", "demo"];

    /// <summary>
    /// Keys whose placeholder value fails the start. Every service checks these; the gateway adds its own
    /// (<c>Keycloak:PublicAuthority</c>, <c>Keycloak:ClientSecret</c>).
    /// </summary>
    public List<string> CriticalKeys { get; } =
    [
        "AllowedHosts",
        "GatewayCallerProof:Secret",
        "Keycloak:Authority",
        "Organization:Name"
    ];

    /// <summary>Critical keys that must also be present and non-empty.</summary>
    public List<string> RequiredKeys { get; } =
    [
        "GatewayCallerProof:Secret",
        "Keycloak:Authority"
    ];

    /// <summary>
    /// File.Storage sets this: with <c>FileStorage:Provider=Minio</c> (its default) the MinIO keys must be real, since
    /// compose cannot require them (MinIO is an optional profile).
    /// </summary>
    public bool ObjectStorageCredentialsRequired { get; set; }

    /// <summary>Substrings that mark a value as a placeholder (case-insensitive).</summary>
    public static readonly string[] PlaceholderMarkers =
    [
        "YOUR_",
        "USE_ENV_OR_",
        "USE_SECRET_MANAGER",
        "CHANGE_ME"
    ];
}
