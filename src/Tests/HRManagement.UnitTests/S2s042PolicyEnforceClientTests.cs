using System.Net;
using System.Text;
using System.Text.Json;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Integration;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-42 §1 (G-POL-03): no denial or outage becomes an allow in Enforce, and Shadow changes nothing.</summary>
public class S2s042PolicyEnforceClientTests
{
    private const string Domain = PolicyDomains.ContractsLeave;

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://hr-core-web:8080") };
    }

    private sealed class CapturingLogger : ILogger<PolicyEnforceClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static PolicyEnforceClient Client(IConfiguration config, HttpMessageHandler handler, ILogger<PolicyEnforceClient>? logger = null) =>
        new(new Factory(handler), new PolicyModeResolver(config), new PolicyClientMetrics(), logger ?? NullLogger<PolicyEnforceClient>.Instance);

    private static HttpResponseMessage Reply(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ServerDecision(bool allowed, PolicyEnforcementMode mode, string? denyReason = null) =>
        Reply(new PolicyEnforceResponse
        {
            Allowed = allowed,
            EffectiveMode = mode,
            Decision = new PolicyDecision
            {
                Allowed = allowed,
                DenyReason = denyReason,
                Effects = [PolicyEffect.RequireEvidence],
                EffectDetails = [new PolicyEffectDefinition { Effect = PolicyEffect.RequireEvidence }]
            }
        });

    private static PolicyEnforceRequest Request(string domain = Domain) =>
        new() { Domain = domain, SubjectType = "LeaveRequest", Action = "Create" };

    [Fact]
    public async Task Enforce_a_denial_with_200_stays_a_denial()
    {
        var client = Client(Config(($"PolicyEngine:{Domain}", "Enforce")),
            new StubHandler(_ => ServerDecision(false, PolicyEnforcementMode.Enforce, "ممنوع بالقاعدة")));

        var result = await client.EnforceAsync(Request());

        Assert.False(result.Allowed);
        Assert.False(result.Degraded);
        Assert.Equal("ممنوع بالقاعدة", result.Decision.DenyReason);
    }

    [Fact]
    public async Task Enforce_an_allow_with_200_is_used_as_it_is_with_its_effects()
    {
        var client = Client(Config(($"PolicyEngine:{Domain}", "Enforce")),
            new StubHandler(_ => ServerDecision(true, PolicyEnforcementMode.Enforce)));

        var result = await client.EnforceAsync(Request());

        Assert.True(result.Allowed);
        Assert.Contains(PolicyEffect.RequireEvidence, result.Decision.Effects);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Enforce_any_status_but_200_is_refused_with_the_message_of_the_plan(HttpStatusCode status)
    {
        var client = Client(Config(($"PolicyEngine:{Domain}", "Enforce")),
            new StubHandler(_ => new HttpResponseMessage(status)));

        var result = await client.EnforceAsync(Request());

        Assert.False(result.Allowed);
        Assert.True(result.Degraded);
        Assert.Equal($"http-{(int)status}", result.FailureReason);
        Assert.StartsWith($"تعذّر التحقق من سياسة {Domain}", result.Decision.DenyReason);
        Assert.False(result.Decision.Allowed);
    }

    [Fact]
    public async Task Enforce_a_timeout_is_refused()
    {
        var client = Client(Config(($"PolicyEngine:{Domain}", "Enforce")),
            new StubHandler(_ => throw new TaskCanceledException("timeout")));

        var result = await client.EnforceAsync(Request());

        Assert.False(result.Allowed);
        Assert.Equal("timeout", result.FailureReason);
    }

    [Fact]
    public async Task Enforce_an_exception_is_refused()
    {
        var client = Client(Config(($"PolicyEngine:{Domain}", "Enforce")),
            new StubHandler(_ => throw new HttpRequestException("refused")));

        var result = await client.EnforceAsync(Request());

        Assert.False(result.Allowed);
        Assert.Equal("exception", result.FailureReason);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"allowed\":true}")]
    [InlineData("{\"allowed\":true,\"decision\":{\"allowed\":true}}")]
    [InlineData("not json")]
    public async Task Enforce_a_200_without_a_decision_and_a_mode_is_a_failure_not_an_allow(string body)
    {
        var client = Client(Config(($"PolicyEngine:{Domain}", "Enforce")),
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));

        var result = await client.EnforceAsync(Request());

        Assert.False(result.Allowed);
        Assert.True(result.Degraded);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AllowWithAlert_lets_the_action_go_on_marked_degraded_and_logs_an_error(HttpStatusCode status)
    {
        var logger = new CapturingLogger();
        var client = Client(
            Config(($"PolicyEngine:{Domain}", "Enforce"), ($"PolicyEngine:FailureMode:{Domain}", "AllowWithAlert")),
            new StubHandler(_ => new HttpResponseMessage(status)),
            logger);

        var result = await client.EnforceAsync(Request());

        Assert.True(result.Allowed);
        Assert.True(result.Degraded);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("policy_client_failure"));
    }

    [Fact]
    public async Task AllowWithAlert_on_a_timeout_goes_on_and_a_server_denial_still_stands()
    {
        var config = Config(($"PolicyEngine:{Domain}", "Enforce"), ($"PolicyEngine:FailureMode:{Domain}", "AllowWithAlert"));

        var timedOut = await Client(config, new StubHandler(_ => throw new TaskCanceledException("timeout"))).EnforceAsync(Request());
        var denied = await Client(config, new StubHandler(_ => ServerDecision(false, PolicyEnforcementMode.Enforce, "no"))).EnforceAsync(Request());

        Assert.True(timedOut.Allowed);
        Assert.True(timedOut.Degraded);
        Assert.False(denied.Allowed);
    }

    [Fact]
    public async Task Shadow_a_server_denial_changes_neither_Allowed_nor_the_effects()
    {
        var client = Client(Config(($"PolicyEngine:{Domain}", "Shadow")),
            new StubHandler(_ => ServerDecision(false, PolicyEnforcementMode.Shadow, "would deny")));

        var result = await client.EnforceAsync(Request());

        Assert.True(result.Allowed);
        Assert.Empty(result.Decision.Effects);
        Assert.Null(result.Decision.DenyReason);
        Assert.Null(result.WorkflowInstanceId);
        Assert.False(result.ShadowDecision!.Allowed);
        Assert.Equal("would deny", result.ShadowDecision.DenyReason);
    }

    [Fact]
    public async Task Shadow_custody_gets_the_same_decision_as_Off()
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["isPrivileged"] = "true", ["value"] = "1" };
        var request = () => new PolicyEnforceRequest { Domain = PolicyDomains.AssetsCustody, Action = "Assign", Facts = new(facts, StringComparer.OrdinalIgnoreCase) };

        var off = await Client(Config(), new StubHandler(_ => throw new InvalidOperationException())).EnforceAsync(request());
        var shadow = await Client(Config(($"PolicyEngine:{PolicyDomains.AssetsCustody}", "Shadow")),
            new StubHandler(_ => ServerDecision(true, PolicyEnforcementMode.Shadow))).EnforceAsync(request());

        Assert.Equal(off.Decision.Effects, shadow.Decision.Effects);
        Assert.Contains(PolicyEffect.RequireEvidence, shadow.Decision.Effects);
        Assert.Contains(PolicyEffect.TriggerWorkflow, shadow.Decision.Effects);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Shadow_an_outage_warns_and_does_not_block(HttpStatusCode status)
    {
        var logger = new CapturingLogger();
        var client = Client(Config(($"PolicyEngine:{Domain}", "Shadow")), new StubHandler(_ => new HttpResponseMessage(status)), logger);

        var result = await client.EnforceAsync(Request());

        Assert.True(result.Allowed);
        Assert.True(result.Degraded);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("policy_client_failure"));
    }

    [Fact]
    public async Task Off_makes_no_call_and_a_neutral_decision_outside_custody()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("no call in Off"));
        var request = Request();
        request.Facts["isPrivileged"] = "true";
        request.Facts["value"] = "99999";

        var result = await Client(Config(), handler).EnforceAsync(request);

        Assert.True(result.Allowed);
        Assert.Equal(0, handler.Calls);
        Assert.Empty(result.Decision.Effects);
        Assert.Equal(PolicyEnforcementMode.Off, result.EffectiveMode);
    }

    [Fact]
    public async Task Off_custody_keeps_the_static_rules()
    {
        var result = await Client(Config(), new StubHandler(_ => throw new InvalidOperationException())).EnforceAsync(new PolicyEnforceRequest
        {
            Domain = PolicyDomains.AssetsCustody,
            Action = "Assign",
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["isPrivileged"] = "true" }
        });

        Assert.Contains(PolicyEffect.RequireEvidence, result.Decision.Effects);
    }

    [Fact]
    public async Task The_body_mode_never_decides_the_local_mode()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException());
        var request = Request();
        request.Mode = PolicyEnforcementMode.Enforce;

        var result = await Client(Config(), handler).EnforceAsync(request);

        Assert.Equal(0, handler.Calls);
        Assert.Equal(PolicyEnforcementMode.Off, result.EffectiveMode);
    }

    [Fact]
    public async Task Mode_mismatch_the_server_mode_reads_the_reply_and_a_warning_is_written()
    {
        var logger = new CapturingLogger();
        var localShadow = Client(Config(($"PolicyEngine:{Domain}", "Shadow")),
            new StubHandler(_ => ServerDecision(false, PolicyEnforcementMode.Enforce, "denied")), logger);

        var enforced = await localShadow.EnforceAsync(Request());

        Assert.False(enforced.Allowed);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("policy_mode_mismatch"));

        var localEnforce = Client(Config(($"PolicyEngine:{Domain}", "Enforce")),
            new StubHandler(_ => ServerDecision(false, PolicyEnforcementMode.Shadow, "would deny")));
        var shadowed = await localEnforce.EnforceAsync(Request());

        Assert.True(shadowed.Allowed);
        Assert.Equal("would deny", shadowed.ShadowDecision!.DenyReason);
    }

    [Fact]
    public async Task Mode_mismatch_the_local_mode_alone_decides_the_failure_path()
    {
        var failing = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        Assert.False((await Client(Config(($"PolicyEngine:{Domain}", "Enforce")), failing).EnforceAsync(Request())).Allowed);
        Assert.True((await Client(Config(($"PolicyEngine:{Domain}", "Shadow")), failing).EnforceAsync(Request())).Allowed);
    }

    [Fact]
    public async Task The_caller_cancelling_is_not_read_as_a_failure()
    {
        using var cts = new CancellationTokenSource();
        var client = Client(Config(($"PolicyEngine:{Domain}", "Enforce")), new StubHandler(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.EnforceAsync(Request(), cts.Token));
    }

    [Theory]
    [InlineData("Off", PolicyEnforcementMode.Off)]
    [InlineData("shadow", PolicyEnforcementMode.Shadow)]
    [InlineData("ENFORCE", PolicyEnforcementMode.Enforce)]
    public void Resolver_order_is_domain_then_default_then_Off(string value, PolicyEnforcementMode expected)
    {
        Assert.Equal(expected, new PolicyModeResolver(Config(($"PolicyEngine:{Domain}", value), ("PolicyEngine:DefaultMode", "Off"))).Resolve(Domain));
        Assert.Equal(expected, new PolicyModeResolver(Config(("PolicyEngine:DefaultMode", value))).Resolve(Domain));
        Assert.Equal(PolicyEnforcementMode.Off, new PolicyModeResolver(Config()).Resolve(Domain));
    }

    [Theory]
    [InlineData("Enforced")]
    [InlineData("1")]
    [InlineData("Off,Enforce")]
    [InlineData("")]
    public void Resolver_an_unknown_value_throws_naming_the_key_not_the_value(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new PolicyModeResolver(Config(($"PolicyEngine:{Domain}", value))).Resolve(Domain));

        Assert.Contains($"PolicyEngine:{Domain}", ex.Message);
    }

    [Fact]
    public void The_default_mode_of_a_request_is_Off() =>
        Assert.Equal(PolicyEnforcementMode.Off, new PolicyEnforceRequest().Mode);

    private static IHostEnvironment Environment(string name) => Mock.Of<IHostEnvironment>(e => e.EnvironmentName == name);

    private static IReadOnlyList<string> Validate(string environment, bool remote, params (string Key, string Value)[] values) =>
        PolicyEngineConfigurationValidator.Evaluate(Config(values), Environment(environment), new PolicyEngineValidationOptions { UsesRemoteClient = remote });

    [Fact]
    public void Boot_accepts_the_valid_settings_and_what_Production_ships_today() =>
        Assert.Empty(Validate("Production", false, ("PolicyEngine:Core.Recruitment", "Shadow"), ("PolicyEngine:DefaultMode", "Off")));

    [Fact]
    public void Boot_refuses_an_unknown_mode_value() =>
        Assert.Contains(Validate("Production", false, ($"PolicyEngine:{Domain}", "Enforced")), f => f.Contains($"PolicyEngine:{Domain}"));

    [Fact]
    public void Boot_refuses_a_key_that_is_not_a_domain() =>
        Assert.Contains(Validate("Production", false, ("PolicyEngine:Contracts.Leeve", "Off")), f => f.Contains("Contracts.Leeve"));

    [Fact]
    public void Boot_refuses_an_unknown_failure_mode() =>
        Assert.Contains(Validate("Staging", false, ($"PolicyEngine:FailureMode:{Domain}", "Open")), f => f.Contains("FailureMode"));

    [Fact]
    public void Boot_refuses_AllowWithAlert_in_Production_and_only_there()
    {
        Assert.Contains(Validate("Production", false, ($"PolicyEngine:FailureMode:{Domain}", "AllowWithAlert")), f => f.Contains("D-POL-2"));
        Assert.Empty(Validate("Development", false, ($"PolicyEngine:FailureMode:{Domain}", "AllowWithAlert")));
    }

    [Fact]
    public void Boot_refuses_a_remote_client_that_is_not_Off_without_the_HR_Core_address()
    {
        Assert.Contains(Validate("Production", true, ($"PolicyEngine:{Domain}", "Shadow")), f => f.Contains("Integration:HrCoreBaseUrl"));
        Assert.Empty(Validate("Production", true, ($"PolicyEngine:{Domain}", "Off")));
        Assert.Empty(Validate("Development", true, ($"PolicyEngine:{Domain}", "Shadow")));
        Assert.Empty(Validate("Production", true, ($"PolicyEngine:{Domain}", "Shadow"), ("Integration:HrCoreBaseUrl", "http://hr-core-web:8080")));
    }

    [Fact]
    public async Task Boot_the_check_throws_and_names_the_key()
    {
        var check = new PolicyEngineConfigurationValidator(
            Config(($"PolicyEngine:{Domain}", "Nope")),
            Environment("Production"),
            Microsoft.Extensions.Options.Options.Create(new PolicyEngineValidationOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => check.RunAsync(CancellationToken.None));

        Assert.Contains($"PolicyEngine:{Domain}", ex.Message);
        Assert.DoesNotContain("Nope", ex.Message);
    }

    [Fact]
    public async Task Classifier_an_outage_leaves_the_asset_as_it_was_and_reports_why()
    {
        var policy = new Mock<IPolicyEnforceClient>();
        policy.Setup(p => p.EnforceAsync(It.IsAny<PolicyEnforceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyEnforceResponse
            {
                Allowed = false,
                Degraded = true,
                Decision = new PolicyDecision { Allowed = false, DenyReason = "outage" }
            });
        var asset = new Asset { Value = 50_000m, IsPrivileged = true };

        var error = await new AssetClassifier(null!, policy.Object).ApplyClassificationAsync(asset);

        Assert.Equal("outage", error);
        Assert.True(asset.IsPrivileged);
    }
}
