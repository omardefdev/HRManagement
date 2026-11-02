using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-39 (section 3): KEYCLOAK_DIRECTORY_SYNC_SECRET is optional in compose (<c>${...:-}</c>); the configuration guard
/// requires <c>Keycloak:DirectorySync:AdminClientSecret</c> only when Auth.Directory provisions users
/// (<c>Keycloak:DirectorySync:Enabled=true</c>), and never prints the value.
/// </summary>
public class Ops039DirectorySyncGuardTests
{
    private static Dictionary<string, string?> AuthDirectory() => new()
    {
        ["AllowedHosts"] = "*",
        ["GatewayCallerProof:Secret"] = "lab-only-caller-proof-secret-0123456789abcdef",
        ["Keycloak:Authority"] = "http://keycloak:8080/realms/hrmanagement",
        ["ConnectionStrings:AuthDirectoryDb"] = "Host=postgresql;Database=auth_directory_db;Username=auth_directory_app;Password=lab-only-0123",
        ["ServiceIdentity:ClientId"] = "svc-auth-directory",
        ["ServiceIdentity:ClientSecret"] = "lab-only-service-secret-0123",
    };

    private static ProductionConfigurationReport Evaluate(Dictionary<string, string?> values) =>
        ProductionConfigurationGuard.Evaluate(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new Ops039HostEnvironment("Production"),
            new ProductionConfigurationGuardOptions());

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Disabled_directory_sync_needs_no_secret(string? enabled)
    {
        var values = AuthDirectory();
        values["Keycloak:DirectorySync:Enabled"] = enabled;
        values["Keycloak:DirectorySync:AdminClientSecret"] = "";
        var report = Evaluate(values);
        Assert.True(report.IsValid, string.Join("; ", report.Failures));
    }

    [Theory]
    [InlineData("")]
    [InlineData("USE_ENV_OR_USER_SECRETS")]
    public void Enabled_directory_sync_without_a_real_secret_stops_the_start(string secret)
    {
        var values = AuthDirectory();
        values["Keycloak:DirectorySync:Enabled"] = "true";
        values["Keycloak:DirectorySync:AdminClientSecret"] = secret;
        var failure = Assert.Single(Evaluate(values).Failures);
        Assert.Equal("Keycloak:DirectorySync:AdminClientSecret", failure.Key);
    }

    [Fact]
    public void Enabled_directory_sync_with_a_secret_passes_and_the_value_is_never_reported()
    {
        const string secret = "lab-only-directory-sync-secret-0123456789";
        var values = AuthDirectory();
        values["Keycloak:DirectorySync:Enabled"] = "true";
        values["Keycloak:DirectorySync:AdminClientSecret"] = secret;
        var report = Evaluate(values);
        Assert.True(report.IsValid, string.Join("; ", report.Failures));
        Assert.DoesNotContain(secret, string.Join("; ", report.Failures.Concat<object>(report.PlaceholderWarnings)), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_compose_makes_the_directory_sync_secret_optional()
    {
        var compose = System.IO.File.ReadAllText(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "docker-compose.production.yml"));
        Assert.Contains("Keycloak__DirectorySync__AdminClientSecret: ${KEYCLOAK_DIRECTORY_SYNC_SECRET:-}", compose);
        Assert.DoesNotContain("KEYCLOAK_DIRECTORY_SYNC_SECRET:?", compose);
    }
}

/// <summary>A minimal <see cref="IHostEnvironment"/> for the PLAN-39 unit tests.</summary>
internal sealed class Ops039HostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "HRManagement.UnitTests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
