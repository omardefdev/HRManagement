using Microsoft.Playwright;

namespace HRManagement.E2E;

public class GatewayUpstreamHealthE2ETests
{
    private static bool ShouldRun =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_PLAYWRIGHT_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("GATEWAY_BASE_URL") ?? "http://localhost:5000";

    private static readonly string[] UpstreamSlugs =
    [
        "hr-core",
        "hr-contracts",
        "hr-health",
        "hr-movements",
        "hr-training",
        "payroll",
        "idcard",
        "records-archive",
        "admin-logistics",
        "it-infra",
        "dev-analytics",
        "compliance",
        "auth-directory"
    ];

    [SkippableFact]
    public async Task Gateway_upstream_health_endpoints_deny_anonymous_access()
    {
        // SEC-001: /health/upstreams/* requires GatewayDashboard — anonymous must not get 200.
        Skip.IfNot(ShouldRun, "Set RUN_PLAYWRIGHT_E2E=true to run E2E tests.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var failures = new List<string>();

        foreach (var slug in UpstreamSlugs)
        {
            var response = await context.APIRequest.GetAsync(
                $"{BaseUrl.TrimEnd('/')}/health/upstreams/{slug}",
                new APIRequestContextOptions { MaxRedirects = 0 });
            // Challenge (401/302) or Forbidden (403) — never open health data.
            if (response.Status is not (401 or 403 or 302))
                failures.Add($"{slug}: expected 401/403/302, got HTTP {response.Status}");
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    [SkippableFact]
    public async Task Gateway_ui_module_paths_return_non_500_when_unauthenticated()
    {
        Skip.IfNot(ShouldRun, "Set RUN_PLAYWRIGHT_E2E=true to run E2E tests.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var failures = new List<string>();

        foreach (var path in new[]
                 {
                     "/ui/hr-core/", "/ui/hr-contracts/", "/ui/payroll/",
                     "/ui/hr-health/", "/ui/hr-training/", "/ui/compliance/"
                 })
        {
            var response = await context.APIRequest.GetAsync($"{BaseUrl.TrimEnd('/')}{path}");
            if (response.Status >= 500)
                failures.Add($"{path}: HTTP {response.Status}");
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
