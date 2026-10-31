using System.Globalization;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HRManagement.UnitTests;

/// <summary>PLAN-36 §7 (G-OPS-15): license checks skip health endpoints and parse the expiry culture-independently.</summary>
public class LicenseValidationMiddlewareTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static async Task<(int Status, bool NextCalled)> InvokeAsync(IConfiguration configuration, string path)
    {
        var nextCalled = false;
        var middleware = new LicenseValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, configuration);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, nextCalled);
    }

    private static readonly (string, string?) Expired = ("Deployment:LicenseExpiresUtc", "2020-01-01T00:00:00Z");

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/ready")]
    [InlineData("/metrics")]
    public async Task Health_and_metrics_pass_with_an_expired_license(string path)
    {
        var (status, nextCalled) = await InvokeAsync(Config(Expired), path);
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/ui/hr-core/")]
    [InlineData("/healthcare")]
    [InlineData("/api/metrics-report")]
    public async Task Other_paths_get_402_with_an_expired_license(string path)
    {
        var (status, nextCalled) = await InvokeAsync(Config(Expired), path);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status402PaymentRequired, status);
    }

    [Fact]
    public async Task Missing_key_gets_402_only_when_required()
    {
        var (status, _) = await InvokeAsync(Config(("Deployment:RequireLicenseKey", "true")), "/");
        Assert.Equal(StatusCodes.Status402PaymentRequired, status);

        (status, var nextCalled) = await InvokeAsync(Config(("Deployment:RequireLicenseKey", "false")), "/");
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);

        (status, _) = await InvokeAsync(Config(("Deployment:RequireLicenseKey", "true")), "/health/live");
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task Future_expiry_passes()
    {
        var future = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var (status, nextCalled) = await InvokeAsync(Config(("Deployment:LicenseExpiresUtc", future)), "/");
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("ar-SY")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void Expiry_is_parsed_the_same_under_any_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // ar-SA uses the Hijri calendar: a culture-sensitive parse of 2027-01-31 would land centuries away.
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.True(LicenseValidationMiddleware.TryParseExpiry("2027-01-31T00:00:00Z", out var withOffset));
            Assert.Equal(new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc), withOffset);
            Assert.Equal(DateTimeKind.Utc, withOffset.Kind);

            Assert.True(LicenseValidationMiddleware.TryParseExpiry("2027-01-31 12:30", out var withoutOffset));
            Assert.Equal(new DateTime(2027, 1, 31, 12, 30, 0, DateTimeKind.Utc), withoutOffset);

            Assert.True(LicenseValidationMiddleware.TryParseExpiry("2027-01-31T03:00:00+03:00", out var shifted));
            Assert.Equal(new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc), shifted);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    public void Unparseable_expiry_is_ignored(string? value)
    {
        Assert.False(LicenseValidationMiddleware.TryParseExpiry(value, out _));
    }

    [Fact]
    public void Employee_cap_claim_is_gone_from_the_middleware()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !System.IO.File.Exists(Path.Combine(root.FullName, "HRManagement.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = System.IO.File.ReadAllText(Path.Combine(
            root!.FullName, "src", "Shared", "HRManagement.ServiceDefaults", "LicenseValidationMiddleware.cs"));
        Assert.DoesNotContain("employee cap", source, StringComparison.OrdinalIgnoreCase);
    }
}
