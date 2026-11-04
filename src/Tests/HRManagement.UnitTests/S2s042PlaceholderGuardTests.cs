using HRManagement.ServiceDefaults;
using HRManagement.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-42 §4: a placeholder in any key stops the start outside Development, Testing and Acceptance, naming the key only.</summary>
public class S2s042PlaceholderGuardTests
{
    private static Dictionary<string, string?> Valid() => new()
    {
        ["AllowedHosts"] = "*",
        ["GatewayCallerProof:PublicKeyPem"] = TestProofKeys.PublicKey,
        ["Keycloak:Authority"] = "http://keycloak:8080/realms/hrmanagement",
        ["Organization:Name"] = "شعبة التنظيم والموارد البشرية"
    };

    private static IHostEnvironment Env(string name) => Mock.Of<IHostEnvironment>(e => e.EnvironmentName == name);

    private static ProductionConfigurationReport Evaluate(Dictionary<string, string?> values, string environment = "Production", ProductionConfigurationGuardOptions? options = null) =>
        ProductionConfigurationGuard.Evaluate(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            Env(environment),
            options ?? new ProductionConfigurationGuardOptions());

    private static ProductionConfigurationGuard Guard(Dictionary<string, string?> values, string environment) =>
        new(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            Env(environment),
            Options.Create(new ProductionConfigurationGuardOptions()),
            NullLogger<ProductionConfigurationGuard>.Instance);

    [Theory]
    [InlineData("USE_ENV_OR_SECRET_MANAGER")]
    [InlineData("USE_ENV_OR_USER_SECRETS")]
    [InlineData("CHANGE_ME")]
    [InlineData("CHANGE_ME_NOW")]
    [InlineData("__SVC_SECRET__")]
    [InlineData("replace-me")]
    [InlineData("YOUR_PORTAL_HOST")]
    public void A_placeholder_in_any_key_fails_in_Production_and_names_the_key(string value)
    {
        var values = Valid();
        values["Notifications:Candidate:Smtp:Password"] = value;

        var failure = Assert.Single(Evaluate(values).Failures);

        Assert.Equal("Notifications:Candidate:Smtp:Password", failure.Key);
        Assert.DoesNotContain(value, failure.ToString());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Acceptance")]
    public void In_the_exempt_environments_it_stays_a_warning(string environment)
    {
        var values = Valid();
        values["Some:Key"] = "USE_ENV_OR_SECRET_MANAGER";

        var report = Evaluate(values, environment);

        Assert.True(report.IsValid, string.Join("; ", report.Failures));
        Assert.Equal(["Some:Key"], report.PlaceholderWarnings);
    }

    [Theory]
    [InlineData("__x")]
    [InlineData("a__b__c")]
    [InlineData("HOST__NAME")]
    [InlineData("http://example.org/__admin__/x")]
    public void An_ordinary_value_with_underscores_is_not_a_placeholder(string value) =>
        Assert.False(ProductionConfigurationGuard.IsPlaceholder(value));

    [Fact]
    public async Task The_message_names_the_key_and_never_the_value()
    {
        var values = Valid();
        values["GatewayCallerProof:PublicKeyPem"] = "USE_ENV_OR_SECRET_MANAGER";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Guard(values, "Production").StartAsync(CancellationToken.None));

        Assert.Contains("GatewayCallerProof:PublicKeyPem", ex.Message);
        Assert.DoesNotContain("USE_ENV_OR_SECRET_MANAGER", ex.Message);
    }

    [Fact]
    public async Task Staging_stops_on_a_placeholder_though_the_rest_of_the_guard_is_off_there()
    {
        var values = new Dictionary<string, string?> { ["Some:Key"] = "CHANGE_ME" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Guard(values, "Staging").StartAsync(CancellationToken.None));

        Assert.Contains("Some:Key", ex.Message);
    }

    [Fact]
    public async Task Staging_without_a_placeholder_starts()
    {
        await Guard(new Dictionary<string, string?> { ["Some:Key"] = "real" }, "Staging").StartAsync(CancellationToken.None);
    }

    [Fact]
    public void Environment_values_override_the_settings_file()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RabbitMQ:Password"] = "USE_ENV_OR_USER_SECRETS", ["RabbitMQ:Username"] = "USE_ENV_OR_USER_SECRETS" })
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RabbitMQ:Password"] = "real-password", ["RabbitMQ:Username"] = "mq_payroll" })
            .AddInMemoryCollection(Valid())
            .Build();

        var report = ProductionConfigurationGuard.Evaluate(configuration, Env("Production"), new ProductionConfigurationGuardOptions());

        Assert.True(report.IsValid, string.Join("; ", report.Failures));
    }

    private static ProductionConfigurationGuardOptions GatewayOptions()
    {
        var options = new ProductionConfigurationGuardOptions();
        options.UseGatewayProofSigner();
        return options;
    }

    [Fact]
    public void The_gateway_needs_the_private_key_and_not_the_public_one()
    {
        var values = Valid();
        values.Remove("GatewayCallerProof:PublicKeyPem");

        Assert.Contains(Evaluate(values, options: GatewayOptions()).Failures, f => f.Key == "GatewayCallerProof:PrivateKeyPem" && f.ReasonEn == "is not set");

        values["GatewayCallerProof:PrivateKeyPem"] = TestProofKeys.PrivateKey;
        Assert.True(Evaluate(values, options: GatewayOptions()).IsValid);
    }

    [Theory]
    [InlineData("0123456789")]
    [InlineData("not base64 at all")]
    public void A_private_key_that_does_not_load_stops_the_gateway(string value)
    {
        var values = Valid();
        values["GatewayCallerProof:PrivateKeyPem"] = value;

        var failure = Assert.Single(Evaluate(values, options: GatewayOptions()).Failures);

        Assert.Equal("GatewayCallerProof:PrivateKeyPem", failure.Key);
        Assert.DoesNotContain(value, failure.ToString());
    }

    [Fact]
    public void A_public_key_in_the_private_setting_of_the_gateway_does_not_load()
    {
        var values = Valid();
        values["GatewayCallerProof:PrivateKeyPem"] = TestProofKeys.PublicKey;

        Assert.Contains(Evaluate(values, options: GatewayOptions()).Failures, f => f.Key == "GatewayCallerProof:PrivateKeyPem");
    }

    [Fact]
    public void A_module_with_the_private_key_refuses_to_start()
    {
        var values = Valid();
        values["GatewayCallerProof:PrivateKeyPem"] = TestProofKeys.PrivateKey;

        var failure = Assert.Single(Evaluate(values).Failures);

        Assert.Equal("GatewayCallerProof:PrivateKeyPem", failure.Key);
    }

    [Fact]
    public void Every_settings_file_that_Production_loads_has_only_the_placeholders_the_server_overrides()
    {
        var root = RepoPaths.Root;
        var found = Directory.GetFiles(Path.Combine(root, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) is "appsettings.json" or "appsettings.Production.json")
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"))
            .SelectMany(file => new ConfigurationBuilder().AddJsonFile(file).Build().AsEnumerable()
                .Where(pair => ProductionConfigurationGuard.IsPlaceholder(pair.Value))
                .Select(pair => pair.Key))
            .Where(key => !key.StartsWith("ConnectionStrings:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // These and the connection strings come from the environment on every deployment; since PLAN-38 the guard stops a service
        // that lacks them. Any other placeholder here would stop a Production start, so it has to be removed or given a value.
        Assert.Equal(["RabbitMQ:Password", "RabbitMQ:Username"], found);
    }
}
