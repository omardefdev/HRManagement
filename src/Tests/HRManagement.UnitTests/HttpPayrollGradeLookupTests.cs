using System.Net;
using System.Text;
using HR.Core.Infrastructure.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>HCM-027 (E2): HTTP adapter for the Payroll pay-grade lookup.</summary>
public class HttpPayrollGradeLookupTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 20);

    [Fact]
    public async Task Ok_response_maps_to_found_and_requests_grade_at_date()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"gradeCode":"G5","gradeNameAr":"الدرجة الخامسة","minSalary":4000.00,"maxSalary":6000.50,"baseSalary":4500,"salaryScaleCode":"SC-2026","scaleEffectiveFrom":"2026-01-01T00:00:00Z","scaleEffectiveTo":null}"""));

        var result = await CreateLookup(handler).GetAsync("G5", AsOf);

        var found = Assert.IsType<PayrollGradeLookupResult.Found>(result);
        Assert.Equal(4000m, found.MinSalary);
        Assert.Equal(6000.50m, found.MaxSalary);
        Assert.Equal(4500m, found.BaseSalary);
        Assert.Equal("الدرجة الخامسة", found.GradeNameAr);
        Assert.Equal("http://payroll.test/api/internal/pay-grades/G5?asOf=2026-11-20", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task Not_found_maps_to_not_found()
    {
        var result = await CreateLookup(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))).GetAsync("G99", AsOf);

        Assert.IsType<PayrollGradeLookupResult.NotFound>(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Server_or_auth_error_maps_to_unavailable(HttpStatusCode status)
    {
        var result = await CreateLookup(new StubHandler(_ => new HttpResponseMessage(status))).GetAsync("G5", AsOf);

        var unavailable = Assert.IsType<PayrollGradeLookupResult.Unavailable>(result);
        Assert.Equal($"HTTP {(int)status}", unavailable.Reason);
    }

    [Fact]
    public async Task Transport_error_maps_to_unavailable()
    {
        var result = await CreateLookup(new StubHandler(_ => throw new HttpRequestException("connection refused"))).GetAsync("G5", AsOf);

        Assert.IsType<PayrollGradeLookupResult.Unavailable>(result);
    }

    [Fact]
    public async Task Timeout_maps_to_unavailable()
    {
        var handler = new DelayHandler(TimeSpan.FromSeconds(5));
        var lookup = CreateLookup(handler, timeout: TimeSpan.FromMilliseconds(100));

        var result = await lookup.GetAsync("G5", AsOf);

        var unavailable = Assert.IsType<PayrollGradeLookupResult.Unavailable>(result);
        Assert.Equal("Timeout", unavailable.Reason);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateLookup(new DelayHandler(TimeSpan.FromSeconds(5))).GetAsync("G5", AsOf, cts.Token));
    }

    [Fact]
    public async Task Unconfigured_base_url_registers_unavailable_lookup()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Integration:PayrollBaseUrl"] = ""
        }).Build();

        services.AddPayrollGradeLookup(configuration);
        using var provider = services.BuildServiceProvider();
        var lookup = provider.GetRequiredService<IPayrollGradeLookup>();

        Assert.IsType<UnavailablePayrollGradeLookup>(lookup);
        var result = Assert.IsType<PayrollGradeLookupResult.Unavailable>(await lookup.GetAsync("G5", AsOf));
        Assert.Equal("Payroll lookup not configured", result.Reason);
    }

    [Fact]
    public void Configured_base_url_registers_http_lookup_with_five_second_timeout()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Integration:PayrollBaseUrl"] = "http://payroll-web:8080/"
        }).Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddPayrollGradeLookup(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<HttpPayrollGradeLookup>(provider.GetRequiredService<IPayrollGradeLookup>());
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpPayrollGradeLookup.HttpClientName);
        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
        Assert.Equal(new Uri("http://payroll-web:8080/"), client.BaseAddress);
    }

    private static HttpPayrollGradeLookup CreateLookup(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://payroll.test/"),
            Timeout = timeout ?? HttpPayrollGradeLookup.Timeout
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(HttpPayrollGradeLookup.HttpClientName)).Returns(client);
        return new HttpPayrollGradeLookup(factory.Object, NullLogger<HttpPayrollGradeLookup>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class DelayHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
