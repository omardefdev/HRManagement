using Microsoft.Playwright;

namespace HRManagement.E2E;

public class GatewayAuthE2ETests
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
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        var finalUrl = page.Url;
        Assert.True(
            finalUrl.Contains("localhost:5000", StringComparison.OrdinalIgnoreCase)
            || finalUrl.Contains("localhost:8080", StringComparison.OrdinalIgnoreCase));
    }

    [SkippableFact]
    public async Task Sso_across_ui_paths_after_login()
    {
        Skip.IfNot(ShouldRun, "Set RUN_PLAYWRIGHT_E2E=true to run E2E tests.");

        var user = Environment.GetEnvironmentVariable("E2E_KEYCLOAK_USER");
        var password = Environment.GetEnvironmentVariable("E2E_KEYCLOAK_PASSWORD");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            Assert.Fail("E2E_KEYCLOAK_USER and E2E_KEYCLOAK_PASSWORD are required when RUN_PLAYWRIGHT_E2E=true for SSO tests.");
            return;
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        await page.GotoAsync(BaseUrl);
        if (page.Url.Contains("8080", StringComparison.OrdinalIgnoreCase))
        {
            await page.FillAsync("input[name='username']", user);
            await page.FillAsync("input[name='password']", password);
            await page.ClickAsync("input[type='submit'], button[type='submit']");
            await page.WaitForURLAsync(u => u.Contains("5000", StringComparison.OrdinalIgnoreCase));
        }

        await page.GotoAsync($"{BaseUrl.TrimEnd('/')}/ui/hr-core/");
        Assert.DoesNotContain("8080/realms", page.Url, StringComparison.OrdinalIgnoreCase);

        await page.GotoAsync($"{BaseUrl.TrimEnd('/')}/ui/payroll/");
        Assert.DoesNotContain("8080/realms", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Logout_clears_session()
    {
        Skip.IfNot(ShouldRun, "Set RUN_PLAYWRIGHT_E2E=true to run E2E tests.");

        var user = Environment.GetEnvironmentVariable("E2E_KEYCLOAK_USER");
        var password = Environment.GetEnvironmentVariable("E2E_KEYCLOAK_PASSWORD");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            Assert.Fail("E2E_KEYCLOAK_USER and E2E_KEYCLOAK_PASSWORD are required when RUN_PLAYWRIGHT_E2E=true for logout tests.");
            return;
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        await page.GotoAsync(BaseUrl);
        if (page.Url.Contains("8080", StringComparison.OrdinalIgnoreCase))
        {
            await page.FillAsync("input[name='username']", user);
            await page.FillAsync("input[name='password']", password);
            await page.ClickAsync("input[type='submit'], button[type='submit']");
            await page.WaitForURLAsync(u => u.Contains("5000", StringComparison.OrdinalIgnoreCase));
        }

        await page.GotoAsync($"{BaseUrl.TrimEnd('/')}/Account/Logout");
        await page.GotoAsync($"{BaseUrl.TrimEnd('/')}/ui/hr-core/");
        Assert.True(
            page.Url.Contains("8080", StringComparison.OrdinalIgnoreCase)
            || page.Url.Contains("signin", StringComparison.OrdinalIgnoreCase));
    }
}
