using System.Net;
using System.Reflection;
using System.Text;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Infrastructure.Services;
using HRManagement.ServiceDefaults.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-42 §7 (G-SEP-12, D-S2S-4): the gate answers only on a clear 200; anything else refuses, at creation and at publication.</summary>
public class S2s042LifecycleGateTests
{
    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public string? Name { get; private set; }

        public HttpClient CreateClient(string name)
        {
            Name = name;
            return new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://hr-core-web:8080/") };
        }
    }

    private static HttpEmployeeLifecycleGate Gate(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new Factory(new Stub(respond)), NullLogger<HttpEmployeeLifecycleGate>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static TheoryData<string> Steps() => new() { "start", "publish" };

    private static Task<(bool Allowed, string? Error)> Check(HttpEmployeeLifecycleGate gate, string step) =>
        step == "start"
            ? gate.AssertCanStartSeparationAsync(Guid.NewGuid(), CancellationToken.None)
            : gate.AssertCanPublishSeparationAsync(Guid.NewGuid(), CancellationToken.None);

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task No_hold_allows(string step)
    {
        var (allowed, error) = await Check(Gate(_ => Json("""{"legalHold":false}""")), step);

        Assert.True(allowed);
        Assert.Null(error);
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task A_hold_refuses(string step)
    {
        var (allowed, error) = await Check(Gate(_ => Json("""{"legalHold":true}""")), step);

        Assert.False(allowed);
        Assert.Equal(HttpEmployeeLifecycleGate.LegalHoldMessage, error);
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task An_unknown_employee_refuses(string step)
    {
        var (allowed, error) = await Check(Gate(_ => new HttpResponseMessage(HttpStatusCode.NotFound)), step);

        Assert.False(allowed);
        Assert.Equal(HttpEmployeeLifecycleGate.NotFoundMessage, error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Any_other_status_refuses_at_both_steps(HttpStatusCode status)
    {
        foreach (var step in new[] { "start", "publish" })
        {
            var (allowed, error) = await Check(Gate(_ => new HttpResponseMessage(status)), step);

            Assert.False(allowed);
            Assert.Equal(HttpEmployeeLifecycleGate.UnverifiedMessage, error);
        }
    }

    [Fact]
    public async Task An_outage_refuses()
    {
        var (allowed, error) = await Check(Gate(_ => throw new HttpRequestException("refused")), "publish");

        Assert.False(allowed);
        Assert.StartsWith("تعذّر التحقق من الحجز القانوني", error);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"legalHold":"no"}""")]
    [InlineData("""{"legalHold":null}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public async Task A_reply_that_does_not_say_refuses(string body)
    {
        var (allowed, error) = await Check(Gate(_ => Json(body)), "start");

        Assert.False(allowed);
        Assert.Equal(HttpEmployeeLifecycleGate.UnverifiedMessage, error);
    }

    [Fact]
    public async Task It_asks_on_the_named_client_with_the_service_identity()
    {
        var stub = new Stub(_ => Json("""{"legalHold":false}"""));
        var factory = new Factory(stub);
        var employee = Guid.NewGuid();

        await new HttpEmployeeLifecycleGate(factory, NullLogger<HttpEmployeeLifecycleGate>.Instance)
            .AssertCanStartSeparationAsync(employee, CancellationToken.None);

        Assert.Equal(HttpEmployeeLifecycleGate.HttpClientName, factory.Name);
        Assert.EndsWith($"/api/internal/employees/{employee}/lifecycle-flags", stub.Seen!.RequestUri!.ToString());
    }

    private static IHostEnvironment Env(string name) => Mock.Of<IHostEnvironment>(e => e.EnvironmentName == name);

    private static EmployeeLifecycleGateStartupCheck StartupCheck(IEmployeeLifecycleGate gate, string environment)
    {
        var services = new ServiceCollection().AddSingleton(gate).BuildServiceProvider();
        return new EmployeeLifecycleGateStartupCheck(services, Env(environment));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Acceptance")]
    public async Task The_pass_through_gate_stops_the_start_outside_Development_and_Testing(string environment)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StartupCheck(new PassThroughEmployeeLifecycleGate(), environment).RunAsync(CancellationToken.None));

        Assert.Contains(HrCoreEndpoint.Key, ex.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task The_pass_through_gate_is_allowed_in_Development_and_Testing(string environment) =>
        await StartupCheck(new PassThroughEmployeeLifecycleGate(), environment).RunAsync(CancellationToken.None);

    [Fact]
    public async Task The_real_gate_starts_anywhere() =>
        await StartupCheck(Gate(_ => Json("{}")), "Production").RunAsync(CancellationToken.None);

    [Fact]
    public void Creation_asks_the_gate()
    {
        var parameters = typeof(CreateSeparationRecordHandler).GetConstructors().Single().GetParameters();

        Assert.Contains(parameters, p => p.ParameterType == typeof(IEmployeeLifecycleGate));
    }
}
