using System.Net;
using System.Security.Cryptography;
using System.Text;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure;
using Admin.Logistics.Infrastructure.Integration;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Infrastructure;
using HR.Contracts.Infrastructure.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>Tests that change process environment variables (DOTNET_RUNNING_IN_CONTAINER, ServiceIdentity__ClientSecret).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "ProcessEnvironment";
}

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

    public EnvironmentVariableScope(params (string Name, string? Value)[] variables)
    {
        foreach (var (name, value) in variables)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _previous)
            Environment.SetEnvironmentVariable(name, value);
    }
}

internal sealed class FakeHostEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
}

/// <summary>PLAN-37 (§4, D-INT-3, D-INT-5): one approved HR.Core key, explicit start-up failure, no localhost in a container.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class HrCoreEndpointTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static readonly IHostEnvironment Production = new FakeHostEnvironment(Environments.Production);
    private static readonly IHostEnvironment Development = new FakeHostEnvironment(Environments.Development);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_url_outside_development_is_a_start_up_error_naming_the_key(string? value)
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", null));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            HrCoreEndpoint.RequireBaseUrl(Config(("Integration:HrCoreBaseUrl", value)), Production));
        Assert.Contains("Integration:HrCoreBaseUrl is required", ex.Message);
    }

    [Fact]
    public void Approved_key_wins_and_loses_its_trailing_slash()
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", "true"));
        Assert.Equal("http://hr-core-web:8080",
            HrCoreEndpoint.RequireBaseUrl(Config(("Integration:HrCoreBaseUrl", "http://hr-core-web:8080/")), Production));
        Assert.Equal(new Uri("http://hr-core-web:8080/"),
            HrCoreEndpoint.RequireBaseUri(Config(("Integration:HrCoreBaseUrl", "http://hr-core-web:8080")), Production));
    }

    [Fact]
    public void Development_outside_a_container_uses_the_local_default()
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", null));
        Assert.Equal(HrCoreEndpoint.DevelopmentDefault, HrCoreEndpoint.RequireBaseUrl(Config(), Development));
    }

    [Fact]
    public void Development_inside_a_container_without_url_is_an_error()
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", "true"));
        var ex = Assert.Throws<InvalidOperationException>(() => HrCoreEndpoint.RequireBaseUrl(Config(), Development));
        Assert.Contains("Integration:HrCoreBaseUrl is required", ex.Message);
    }

    [Theory]
    [InlineData("http://localhost:5010")]
    [InlineData("http://127.0.0.1:5010")]
    public void Localhost_inside_a_container_is_refused(string url)
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", "true"));
        foreach (var environment in new[] { Production, Development })
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                HrCoreEndpoint.RequireBaseUrl(Config(("Integration:HrCoreBaseUrl", url)), environment));
            Assert.Contains("Docker service DNS", ex.Message);
            Assert.Contains("Integration:HrCoreBaseUrl", ex.Message);
        }
    }

    [Fact]
    public void Localhost_outside_a_container_is_accepted()
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", null));
        Assert.Equal("http://localhost:5010",
            HrCoreEndpoint.RequireBaseUrl(Config(("Integration:HrCoreBaseUrl", "http://localhost:5010")), Production));
    }

    [Fact]
    public void Relative_url_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            HrCoreEndpoint.RequireBaseUrl(Config(("Integration:HrCoreBaseUrl", "hr-core-web:8080")), Production));
        Assert.Contains("absolute http(s) URL", ex.Message);
    }

    [Theory]
    [InlineData("Services:HRCore:BaseUrl")]
    [InlineData("HrCore:BaseUrl")]
    public void Deprecated_key_works_with_one_start_up_warning(string legacyKey)
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", "true"));
        Assert.Equal("http://hr-core-web:8080",
            HrCoreEndpoint.RequireBaseUrl(Config((legacyKey, "http://hr-core-web:8080")), Production));
        Assert.Contains(ServiceEndpoint.DeprecationWarnings, w => w.Contains(legacyKey) && w.Contains("Integration:HrCoreBaseUrl"));

        var services = new ServiceCollection();
        var logger = new CapturingLoggerProvider();
        services.AddLogging(b => b.AddProvider(logger));
        services.AddServiceEndpointWarnings();
        services.AddServiceEndpointWarnings();
        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();
        Assert.Single(hosted);
        hosted[0].StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        hosted[0].StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Single(logger.Messages, m => m.Contains(legacyKey));
    }

    [Fact]
    public void Deprecated_key_that_disagrees_with_the_approved_key_is_an_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HrCoreEndpoint.RequireBaseUrl(
            Config(("Integration:HrCoreBaseUrl", "http://hr-core-web:8080"), ("Services:HRCore:BaseUrl", "http://other:8080")),
            Production));
        Assert.Contains("disagree", ex.Message);
        Assert.Contains("Services:HRCore:BaseUrl", ex.Message);
    }

    [Fact]
    public void Code_generation_registration_without_url_fails_outside_development()
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", null), ("ASPNETCORE_ENVIRONMENT", null), ("DOTNET_ENVIRONMENT", null));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddHrCodeGeneration(Config(("environment", "Production"))));
        Assert.Contains("Integration:HrCoreBaseUrl", ex.Message);

        var development = Config(("environment", "Development"));
        var services = new ServiceCollection().AddSingleton(development).AddLogging().AddHrCodeGeneration(development);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(CodeGenerationClient.HttpClientName);
        Assert.Equal(new Uri("http://localhost:5010/"), client.BaseAddress);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Capturing(Messages);
        public void Dispose() { }

        private sealed class Capturing(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                    messages.Add(formatter(state, exception));
            }
        }
    }
}

/// <summary>PLAN-37 (§4): a blank secret in appsettings.json no longer hides the environment variable.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ServiceIdentitySecretTests
{
    private static IConfiguration Config(string? secret) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceIdentity:ClientSecret"] = secret })
        .Build();

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Blank_json_secret_falls_back_to_the_environment_variable(string? jsonValue)
    {
        using var _ = new EnvironmentVariableScope(("ServiceIdentity__ClientSecret", "secret-from-environment"));
        Assert.Equal("secret-from-environment", ClientCredentialsTokenProvider.ResolveClientSecret(Config(jsonValue)));
    }

    [Fact]
    public void Configured_secret_wins()
    {
        using var _ = new EnvironmentVariableScope(("ServiceIdentity__ClientSecret", "secret-from-environment"));
        Assert.Equal("configured", ClientCredentialsTokenProvider.ResolveClientSecret(Config("configured")));
    }

    [Fact]
    public async Task Blank_everywhere_is_an_error_before_any_token_request()
    {
        using var _ = new EnvironmentVariableScope(("ServiceIdentity__ClientSecret", " "));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceIdentity:ClientId"] = "svc-hr-contracts",
            ["ServiceIdentity:ClientSecret"] = ""
        }).Build();
        var services = new ServiceCollection();
        services.AddHttpClient();
        using var provider = services.BuildServiceProvider();
        var tokenProvider = new ClientCredentialsTokenProvider(
            provider.GetRequiredService<IHttpClientFactory>(), configuration, NullLogger<ClientCredentialsTokenProvider>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tokenProvider.GetAccessTokenAsync());
        Assert.Contains("ServiceIdentity:ClientSecret is required", ex.Message);
    }
}

/// <summary>PLAN-37 (§4): the legal-hold gate is wired by the approved key (its authentication is PLAN-42).</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class Int037LifecycleGateRegistrationTests
{
    private static ServiceProvider Build(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmployeeLifecycleGate(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Approved_key_alone_registers_the_http_gate_with_the_hr_core_address()
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", "true"));
        using var provider = Build(("environment", "Production"), ("Integration:HrCoreBaseUrl", "http://hr-core-web:8080"));
        using var scope = provider.CreateScope();
        Assert.IsType<HttpEmployeeLifecycleGate>(scope.ServiceProvider.GetRequiredService<IEmployeeLifecycleGate>());
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpEmployeeLifecycleGate.HttpClientName);
        Assert.Equal(new Uri("http://hr-core-web:8080/"), client.BaseAddress);
    }

    [Fact]
    public void Old_key_still_registers_the_http_gate()
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", "true"));
        using var provider = Build(("environment", "Production"), ("Services:HRCore:BaseUrl", "http://hr-core-web:8080"));
        using var scope = provider.CreateScope();
        Assert.IsType<HttpEmployeeLifecycleGate>(scope.ServiceProvider.GetRequiredService<IEmployeeLifecycleGate>());
    }

    [Fact]
    public void Missing_url_in_production_is_a_start_up_error_not_a_pass_through_gate()
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", "true"));
        var ex = Assert.Throws<InvalidOperationException>(() => Build(("environment", "Production")));
        Assert.Contains("Integration:HrCoreBaseUrl", ex.Message);
    }

    [Fact]
    public void No_configuration_keeps_the_pass_through_gate_for_unit_tests()
    {
        var services = new ServiceCollection();
        services.AddEmployeeLifecycleGate(null);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<PassThroughEmployeeLifecycleGate>(provider.GetRequiredService<IEmployeeLifecycleGate>());
    }
}

/// <summary>PLAN-37 (§4): the asset workflow start carries the gateway caller proof for /api/workflow-instances.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class Int037WorkflowTriggerProofTests
{
    private const string Secret = "unit-test-caller-proof-secret-0123456789";

    [Fact]
    public async Task Both_workflow_starts_sign_the_caller_proof()
    {
        using var _ = new EnvironmentVariableScope(("DOTNET_RUNNING_IN_CONTAINER", "true"));
        var handler = new RecordingHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["environment"] = "Production",
            ["Integration:HrCoreBaseUrl"] = "http://hr-core-web:8080",
            ["GatewayCallerProof:Secret"] = Secret
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHrCoreWorkflowClient(configuration);
        services.AddHttpClient(WorkflowTriggerService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IServiceTokenProvider>(new StaticToken());
        services.AddScoped<WorkflowTriggerService>();
        using var provider = services.BuildServiceProvider();
        var trigger = provider.CreateScope().ServiceProvider.GetRequiredService<WorkflowTriggerService>();

        await trigger.StartAssetAssignmentWorkflowAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "TAG-1", false, 10m);
        await trigger.StartAssetLossWorkflowAsync(Guid.NewGuid(), Guid.NewGuid(), "TAG-1", false, 10m, AssetLossReason.Theft);

        Assert.Equal(2, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            Assert.Equal(new Uri("http://hr-core-web:8080/api/workflow-instances"), request.RequestUri);
            var ts = request.Headers.GetValues(GatewayCallerProof.TimestampHeader).Single();
            Assert.Equal("/api/workflow-instances", request.Headers.GetValues(GatewayCallerProof.PathHeader).Single());
            var expected = Convert.ToBase64String(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{ts}\nPOST\n/api/workflow-instances")));
            Assert.Equal(expected, request.Headers.GetValues(GatewayCallerProof.ProofHeader).Single());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        }
    }

    private sealed class StaticToken : IServiceTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("token");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent($"{{\"id\":\"{Guid.NewGuid()}\"}}", Encoding.UTF8, "application/json")
            });
        }
    }
}
