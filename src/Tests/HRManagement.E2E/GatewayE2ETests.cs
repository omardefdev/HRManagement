using Microsoft.Playwright;

namespace HRManagement.E2E;

[Collection("Playwright")]
public class GatewayE2ETests
{
    private readonly PlaywrightFixture _fx;
    public GatewayE2ETests(PlaywrightFixture fx) => _fx = fx;

    private static bool ShouldRun =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_PLAYWRIGHT_E2E"), "true", StringComparison.OrdinalIgnoreCase);

    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("GATEWAY_BASE_URL") ?? "http://localhost:5000";

    [SkippableFact]
    public async Task Gateway_login_redirect_smoke()
    {
        Skip.IfNot(ShouldRun, "Set RUN_PLAYWRIGHT_E2E=true to run E2E tests.");

        // GatewayAuthE2ETests has a test of the same name; the class name keeps their trace files apart.
        await _fx.RunTracedAsync($"{nameof(GatewayE2ETests)}.{nameof(Gateway_login_redirect_smoke)}", async (_, page) =>
        {
            await page.GotoAsync(BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });

            var finalUrl = page.Url;
            Assert.True(
                E2eEnv.IsGatewayUrl(finalUrl) || E2eEnv.IsKeycloakLoginUrl(finalUrl),
                $"Expected gateway or Keycloak redirect, got {finalUrl}");
        });
    }

    [SkippableFact]
    public async Task Gateway_health_smoke_when_stack_running()
    {
        Skip.IfNot(ShouldRun, "Set RUN_PLAYWRIGHT_E2E=true to run E2E tests.");

        await _fx.RunTracedAsync(nameof(Gateway_health_smoke_when_stack_running), async (_, page) =>
        {
            var response = await page.GotoAsync($"{BaseUrl.TrimEnd('/')}/health/live", new() { WaitUntil = WaitUntilState.DOMContentLoaded });

            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);
        });
    }
}
