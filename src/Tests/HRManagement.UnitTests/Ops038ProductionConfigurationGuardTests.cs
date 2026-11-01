using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-38 (section 3): the guard also covers what a service connects with (its connection string, its broker user and
/// password, its service-identity secret) and the postures of the single host (D-38-1, D-38-2). The AC-OPS-38g
/// negatives in unit form: no caller-proof secret, or a connection string with USE_ENV_, stops the start. Every failure
/// names the key and never the value.
/// </summary>
public class Ops038ProductionConfigurationGuardTests
{
    private const string GoodSecret = "lab-only-caller-proof-secret-0123456789abcdef";
    private const string AppConnection = "Host=postgresql;Port=5432;Database=payroll_db;Username=payroll_app;Password=lab-only-db-password-0123";

    private static Dictionary<string, string?> ValidService() => new()
    {
        ["AllowedHosts"] = "*",
        ["GatewayCallerProof:Secret"] = GoodSecret,
        ["Keycloak:Authority"] = "http://keycloak:8080/realms/hrmanagement",
        ["Organization:Name"] = "شعبة التنظيم والموارد البشرية",
        ["ConnectionStrings:PayrollDb"] = AppConnection,
        ["RabbitMQ:Host"] = "rabbitmq",
        ["RabbitMQ:Username"] = "mq_payroll",
        ["RabbitMQ:Password"] = "lab-only-broker-password-0123",
        ["ServiceIdentity:ClientId"] = "svc-payroll",
        ["ServiceIdentity:ClientSecret"] = "lab-only-service-secret-0123",
        ["Deployment:MqPosture"] = "least-privilege",
        ["Deployment:DbPosture"] = "least-privilege",
    };

    private static ProductionConfigurationReport Evaluate(
        Dictionary<string, string?> values,
        string environment = "Production",
        ProductionConfigurationGuardOptions? options = null) =>
        ProductionConfigurationGuard.Evaluate(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new TestHostEnvironment(environment),
            options ?? new ProductionConfigurationGuardOptions());

    private static void AssertSingleFailure(ProductionConfigurationReport report, string key, string? secretValue)
    {
        var failure = Assert.Single(report.Failures);
        Assert.Equal(key, failure.Key);
        if (!string.IsNullOrEmpty(secretValue))
            Assert.DoesNotContain(secretValue, failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_least_privilege_service_configuration_passes()
    {
        var report = Evaluate(ValidService());
        Assert.True(report.IsValid, string.Join("; ", report.Failures));
    }

    [Fact]
    public void Missing_caller_proof_secret_stops_the_start()
    {
        var values = ValidService();
        values.Remove("GatewayCallerProof:Secret");
        AssertSingleFailure(Evaluate(values), "GatewayCallerProof:Secret", null);
    }

    [Theory]
    [InlineData("Host=localhost;Port=5433;Database=payroll_db;Username=hrmanagement;Password=USE_ENV_OR_USER_SECRETS")]
    [InlineData("")]
    public void A_connection_string_with_a_placeholder_or_no_value_stops_the_start(string connectionString)
    {
        var values = ValidService();
        values["ConnectionStrings:PayrollDb"] = connectionString;
        AssertSingleFailure(Evaluate(values), "ConnectionStrings:PayrollDb", connectionString.Length > 0 ? "USE_ENV_OR_USER_SECRETS" : null);
    }

    [Theory]
    [InlineData("RabbitMQ:Username", "USE_ENV_OR_USER_SECRETS")]
    [InlineData("RabbitMQ:Password", "USE_ENV_OR_USER_SECRETS")]
    [InlineData("RabbitMQ:Password", "")]
    [InlineData("ServiceIdentity:ClientSecret", "")]
    [InlineData("ServiceIdentity:ClientSecret", "USE_ENV_OR_SECRET_MANAGER")]
    public void Broker_and_service_identity_credentials_must_be_real(string key, string value)
    {
        var values = ValidService();
        values[key] = value;
        AssertSingleFailure(Evaluate(values), key, value.Length > 0 ? value : null);
    }

    [Fact]
    public void A_service_without_a_bus_or_a_client_id_needs_neither_credential()
    {
        var values = ValidService();
        foreach (var key in values.Keys.Where(k => k.StartsWith("RabbitMQ:", StringComparison.Ordinal) || k.StartsWith("ServiceIdentity:", StringComparison.Ordinal)).ToList())
            values.Remove(key);
        Assert.True(Evaluate(values).IsValid);
    }

    [Fact]
    public void The_broker_administrator_is_refused_in_the_least_privilege_posture_and_accepted_only_with_admin()
    {
        var values = ValidService();
        values["RabbitMQ:Username"] = "hrm_admin";
        AssertSingleFailure(Evaluate(values), "RabbitMQ:Username", null);

        values["Deployment:MqPosture"] = "admin";
        Assert.True(Evaluate(values).IsValid);

        values["Deployment:MqPosture"] = "relaxed";
        AssertSingleFailure(Evaluate(values), "Deployment:MqPosture", null);
    }

    [Fact]
    public void The_database_administrator_or_migrator_is_refused_in_the_least_privilege_posture()
    {
        var values = ValidService();
        values["ConnectionStrings:PayrollDb"] = AppConnection.Replace("payroll_app", "payroll_migrator", StringComparison.Ordinal);
        AssertSingleFailure(Evaluate(values), "ConnectionStrings:PayrollDb", "lab-only-db-password-0123");

        values["ConnectionStrings:PayrollDb"] = AppConnection.Replace("payroll_app", "hrm_admin", StringComparison.Ordinal);
        AssertSingleFailure(Evaluate(values), "ConnectionStrings:PayrollDb", "lab-only-db-password-0123");
    }

    [Fact]
    public void The_demo_database_posture_exists_only_in_demo_mode()
    {
        var values = ValidService();
        values["ConnectionStrings:PayrollDb"] = AppConnection.Replace("payroll_app", "hrm_admin", StringComparison.Ordinal);
        values["Deployment:DbPosture"] = "demo";
        AssertSingleFailure(Evaluate(values), "Deployment:DbPosture", null);

        values["Deployment:Mode"] = "demo";
        values["Seed:Profile"] = "Demo";
        Assert.True(Evaluate(values).IsValid, string.Join("; ", Evaluate(values).Failures));
    }

    [Fact]
    public void A_localhost_authority_is_refused_in_production_only()
    {
        var values = ValidService();
        values["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement";
        AssertSingleFailure(Evaluate(values), "Keycloak:Authority", null);

        var options = new ProductionConfigurationGuardOptions();
        Assert.True(Evaluate(values, "Acceptance", options).IsValid);
    }

    [Fact]
    public void Hr_core_otp_key_is_critical_when_hr_core_adds_it()
    {
        var options = new ProductionConfigurationGuardOptions();
        options.CriticalKeys.Add("Recruitment:Portal:OtpHmacKey");
        options.RequiredKeys.Add("Recruitment:Portal:OtpHmacKey");
        var values = ValidService();

        AssertSingleFailure(Evaluate(values, options: options), "Recruitment:Portal:OtpHmacKey", null);

        values["Recruitment:Portal:OtpHmacKey"] = "lab-only-otp-hmac-key-0123456789abcdef";
        Assert.True(Evaluate(values, options: options).IsValid);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HRManagement.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
