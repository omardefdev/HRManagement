using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>PLAN-36 §5: the production configuration guard names the failing key and never prints a value.</summary>
public class Ops036ProductionConfigurationGuardTests
{
    private const string GoodSecret = "lab-only-caller-proof-secret-0123456789abcdef";

    private static Dictionary<string, string?> ValidModule() => new()
    {
        ["AllowedHosts"] = "*",
        ["GatewayCallerProof:Secret"] = GoodSecret,
        ["Keycloak:Authority"] = "http://keycloak:8080/realms/hrmanagement",
        ["Organization:Name"] = "شعبة التنظيم والموارد البشرية"
    };

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ProductionConfigurationReport Evaluate(
        Dictionary<string, string?> values,
        string environment = "Production",
        ProductionConfigurationGuardOptions? options = null) =>
        ProductionConfigurationGuard.Evaluate(Config(values), new TestHostEnvironment(environment), options ?? new());

    private static ProductionConfigurationGuardOptions GatewayOptions()
    {
        var options = new ProductionConfigurationGuardOptions();
        options.CriticalKeys.Add("Keycloak:PublicAuthority");
        options.CriticalKeys.Add("Keycloak:ClientSecret");
        options.RequiredKeys.Add("Keycloak:PublicAuthority");
        options.RequiredKeys.Add("Keycloak:ClientSecret");
        return options;
    }

    [Fact]
    public void Valid_module_configuration_passes()
    {
        var report = Evaluate(ValidModule());
        Assert.True(report.IsValid, string.Join("; ", report.Failures));
    }

    [Theory]
    [InlineData("AllowedHosts", "YOUR_MODULE_HOST")]
    [InlineData("GatewayCallerProof:Secret", "USE_ENV_OR_SECRET_MANAGER")]
    [InlineData("Keycloak:Authority", "https://YOUR_KEYCLOAK_HOST/realms/hrmanagement")]
    [InlineData("Organization:Name", "CHANGE_ME")]
    public void Placeholder_in_a_critical_key_fails_and_names_the_key(string key, string placeholder)
    {
        var values = ValidModule();
        values[key] = placeholder;

        var report = Evaluate(values);

        var failure = Assert.Single(report.Failures);
        Assert.Equal(key, failure.Key);
        Assert.DoesNotContain(placeholder, failure.ToString());
    }

    [Fact]
    public void Placeholder_fails_in_production_but_passes_in_development()
    {
        var values = ValidModule();
        values["AllowedHosts"] = "YOUR_MODULE_HOST";
        var development = new TestHostEnvironment("Development");

        Assert.False(ProductionConfigurationGuard.IsEnforced(Config(values), development));
        Assert.True(ProductionConfigurationGuard.IsEnforced(Config(values), new TestHostEnvironment("Production")));
        Assert.False(ProductionConfigurationGuard.IsEnforced(Config(values), new TestHostEnvironment("Acceptance")));
    }

    [Fact]
    public async Task Guard_does_nothing_in_development_and_acceptance_without_the_explicit_switch()
    {
        var values = ValidModule();
        values["AllowedHosts"] = "YOUR_MODULE_HOST";

        foreach (var environment in new[] { "Development", "Acceptance" })
            await CreateGuard(values, environment).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Explicit_switch_enforces_the_guard_outside_production()
    {
        var values = ValidModule();
        values["AllowedHosts"] = "YOUR_MODULE_HOST";
        values[ProductionConfigurationGuardOptions.EnforceKey] = "true";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateGuard(values, "Acceptance").StartAsync(CancellationToken.None));
        Assert.Contains("AllowedHosts", ex.Message);
    }

    [Fact]
    public async Task Production_cannot_switch_the_guard_off()
    {
        var values = ValidModule();
        values["AllowedHosts"] = "YOUR_MODULE_HOST";
        values[ProductionConfigurationGuardOptions.EnforceKey] = "false";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateGuard(values, "Production").StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Failure_message_is_bilingual_names_the_key_and_never_prints_the_secret()
    {
        var values = ValidModule();
        const string shortSecret = "short-0123";
        values["GatewayCallerProof:Secret"] = shortSecret;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateGuard(values, "Production").StartAsync(CancellationToken.None));

        Assert.Contains("GatewayCallerProof:Secret", ex.Message);
        Assert.Contains("32", ex.Message);
        Assert.Contains("لن تبدأ الخدمة", ex.Message);
        Assert.Contains("will not start", ex.Message);
        Assert.DoesNotContain(shortSecret, ex.Message);
    }

    [Theory]
    [InlineData("GatewayCallerProof:Secret")]
    [InlineData("Keycloak:Authority")]
    public void Missing_required_key_fails(string key)
    {
        var values = ValidModule();
        values.Remove(key);

        var report = Evaluate(values);

        Assert.Contains(report.Failures, f => f.Key == key && f.ReasonEn == "is not set");
    }

    [Fact]
    public void Caller_proof_secret_of_exactly_32_characters_passes()
    {
        var values = ValidModule();
        values["GatewayCallerProof:Secret"] = new string('x', 32);
        Assert.True(Evaluate(values).IsValid);

        values["GatewayCallerProof:Secret"] = new string('x', 31);
        Assert.Contains(Evaluate(values).Failures, f => f.Key == "GatewayCallerProof:Secret");
    }

    [Fact]
    public void Gateway_requires_public_authority_and_client_secret()
    {
        var report = Evaluate(ValidModule(), options: GatewayOptions());

        Assert.Contains(report.Failures, f => f.Key == "Keycloak:PublicAuthority");
        Assert.Contains(report.Failures, f => f.Key == "Keycloak:ClientSecret");
    }

    [Theory]
    [InlineData("http://localhost:8088/realms/hrmanagement")]
    [InlineData("http://127.0.0.1:8088/realms/hrmanagement")]
    public void Localhost_public_authority_fails_in_production_only(string authority)
    {
        var values = ValidModule();
        values["Keycloak:PublicAuthority"] = authority;
        values["Keycloak:ClientSecret"] = "lab-only-client-secret";

        Assert.Contains(
            Evaluate(values, options: GatewayOptions()).Failures,
            f => f.Key == "Keycloak:PublicAuthority");
        Assert.True(Evaluate(values, "Acceptance", GatewayOptions()).IsValid);
    }

    [Fact]
    public void Public_https_authority_passes_for_the_gateway()
    {
        var values = ValidModule();
        values["Keycloak:PublicAuthority"] = "https://sso.161-97-185-71.sslip.io/realms/hrmanagement";
        values["Keycloak:ClientSecret"] = "lab-only-client-secret";
        values["Gateway:EnabledModules:0"] = "hr-core";
        values["Gateway:EnabledModules:1"] = "payroll";

        var report = Evaluate(values, options: GatewayOptions());
        Assert.True(report.IsValid, string.Join("; ", report.Failures));
    }

    [Fact]
    public void Empty_gateway_module_entry_fails()
    {
        var values = ValidModule();
        values["Gateway:EnabledModules:0"] = "hr-core";
        values["Gateway:EnabledModules:1"] = "";

        Assert.Contains(Evaluate(values).Failures, f => f.Key == "Gateway:EnabledModules");
    }

    [Theory]
    [InlineData("Organization:Name", "Ø´Ø¹Ø¨Ø© Ø§Ù„ØªÙ†Ø¸ÙŠÙ…")]
    [InlineData("Organization:ShortName", "Ã˜Â§Ã™â€žÃ™â€¦")]
    public void Garbled_organization_name_fails(string key, string garbled)
    {
        var values = ValidModule();
        values[key] = garbled;

        Assert.Contains(Evaluate(values).Failures, f => f.Key == key);
    }

    [Theory]
    [InlineData("شعبة التنظيم والموارد البشرية")]
    [InlineData("Ministère de la Santé")]
    [InlineData("Ministry of Health")]
    public void Real_organization_names_pass(string name)
    {
        var values = ValidModule();
        values["Organization:Name"] = name;
        Assert.True(Evaluate(values).IsValid);
    }

    [Fact]
    public void Demo_mode_without_a_seed_profile_fails()
    {
        var values = ValidModule();
        values[ProductionConfigurationGuardOptions.DeploymentModeKey] = "demo";

        Assert.Contains(Evaluate(values).Failures, f => f.Key == "Seed:Profile");

        values["Seed:Profile"] = "None";
        Assert.Contains(Evaluate(values).Failures, f => f.Key == "Seed:Profile");

        values["Seed:Profile"] = "Demo";
        Assert.True(Evaluate(values).IsValid);
    }

    [Fact]
    public void File_storage_on_minio_requires_real_keys_but_local_does_not()
    {
        var options = new ProductionConfigurationGuardOptions { ObjectStorageCredentialsRequired = true };
        var values = ValidModule();
        values["FileStorage:Provider"] = "Minio";
        values["FileStorage:AccessKey"] = "";
        values["FileStorage:SecretKey"] = "USE_ENV_OR_USER_SECRETS";

        var report = Evaluate(values, options: options);
        Assert.Contains(report.Failures, f => f.Key == "FileStorage:AccessKey");
        Assert.Contains(report.Failures, f => f.Key == "FileStorage:SecretKey");

        values["FileStorage:Provider"] = "Local";
        report = Evaluate(values, options: options);
        Assert.True(report.IsValid);
        Assert.Contains("FileStorage:SecretKey", report.PlaceholderWarnings);
    }

    [Fact]
    public void Placeholder_in_a_non_critical_key_is_only_a_warning()
    {
        var values = ValidModule();
        values["Cors:AllowedOrigins:0"] = "https://YOUR_PORTAL_HOST";

        var report = Evaluate(values);

        Assert.True(report.IsValid);
        Assert.Equal(["Cors:AllowedOrigins:0"], report.PlaceholderWarnings);
    }

    [Fact]
    public void Environment_override_wins_over_the_placeholder_in_the_settings_file()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "YOUR_MODULE_HOST",
                ["GatewayCallerProof:Secret"] = "USE_ENV_OR_SECRET_MANAGER",
                ["Keycloak:Authority"] = "https://YOUR_KEYCLOAK_HOST/realms/hrmanagement"
            })
            .AddInMemoryCollection(ValidModule())
            .Build();

        var report = ProductionConfigurationGuard.Evaluate(
            configuration, new TestHostEnvironment("Production"), new ProductionConfigurationGuardOptions());

        Assert.True(report.IsValid, string.Join("; ", report.Failures));
    }

    [Fact]
    public void Guard_is_registered_by_the_shared_production_networking()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !System.IO.File.Exists(Path.Combine(root.FullName, "HRManagement.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var source = System.IO.File.ReadAllText(Path.Combine(
            root!.FullName, "src", "Shared", "HRManagement.ServiceDefaults", "ProductionHostExtensions.cs"));
        Assert.Contains("AddHostedService<ProductionConfigurationGuard>()", source);

        var programs = Directory.GetFiles(Path.Combine(root!.FullName, "src"), "Program.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && System.IO.File.ReadAllText(p).Contains("AddHrProductionNetworking()"))
            .ToList();
        Assert.Equal(15, programs.Count);
    }

    [Fact]
    public void Gateway_default_organization_name_is_readable_arabic()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !System.IO.File.Exists(Path.Combine(root.FullName, "HRManagement.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root!.FullName, "src", "Gateway", "HRManagement.Gateway", "appsettings.json"))
            .Build();

        Assert.Equal("شعبة التنظيم والموارد البشرية", configuration["Organization:Name"]);
        Assert.Equal("الموارد البشرية", configuration["Organization:ShortName"]);
    }

    private static ProductionConfigurationGuard CreateGuard(Dictionary<string, string?> values, string environment) =>
        new(
            Config(values),
            new TestHostEnvironment(environment),
            Options.Create(new ProductionConfigurationGuardOptions()),
            NullLogger<ProductionConfigurationGuard>.Instance);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
