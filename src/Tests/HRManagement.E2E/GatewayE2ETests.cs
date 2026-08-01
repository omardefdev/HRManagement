using Microsoft.Playwright;

namespace HRManagement.E2E;

public class GatewayE2ETests
{
    private static bool ShouldRun =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_PLAYWRIGHT_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("GATEWAY_BASE_URL") ?? "http://localhost:5000";

    [SkippableFact]
    public async Task Gateway_login_redirect_smoke()
    {
        Skip.IfNot(ShouldRun, "Set RUN_PLAYWRIGHT_E2E=true to run E2E tests.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        var finalUrl = page.Url;
        Assert.True(
            finalUrl.Contains("localhost:5000", StringComparison.OrdinalIgnoreCase)
            || finalUrl.Contains("localhost:8080", StringComparison.OrdinalIgnoreCase));
    }

    [SkippableFact]
    public async Task Gateway_health_smoke_when_stack_running()
    {
        Skip.IfNot(ShouldRun, "Set RUN_PLAYWRIGHT_E2E=true to run E2E tests.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var response = await page.GotoAsync($"{BaseUrl.TrimEnd('/')}/health/live", new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);
    }
}
