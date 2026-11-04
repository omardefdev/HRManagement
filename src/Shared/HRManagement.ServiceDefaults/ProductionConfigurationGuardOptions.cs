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
        GatewayCallerProofOptions.PublicKeyKey,
        "Keycloak:Authority",
        "Organization:Name"
    ];

    /// <summary>Critical keys that must also be present and non-empty.</summary>
    public List<string> RequiredKeys { get; } =
    [
        GatewayCallerProofOptions.PublicKeyKey,
        "Keycloak:Authority"
    ];

    /// <summary>
    /// PLAN-42: the gateway holds the private key and every module the public key. Only the gateway sets this; a module with a
    /// private key refuses to start, because that key is what makes a proof.
    /// </summary>
    public bool SignsGatewayProof { get; private set; }

    public void UseGatewayProofSigner()
    {
        SignsGatewayProof = true;
        foreach (var keys in new[] { CriticalKeys, RequiredKeys })
        {
            keys.Remove(GatewayCallerProofOptions.PublicKeyKey);
            keys.Add(GatewayCallerProofOptions.PrivateKeyKey);
        }
    }

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
