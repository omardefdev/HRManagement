using Microsoft.Playwright;

namespace HRManagement.E2E;

/// <summary>Playwright browser/API fixture with failure traces and screenshots (QA-004-B).</summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IPlaywright Playwright => _playwright ?? throw new InvalidOperationException("Not started");
    public IBrowser Browser => _browser ?? throw new InvalidOperationException("Not started");

    public async Task InitializeAsync()
    {
        if (!E2eEnv.IsEnabled)
            return;
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    public async Task<IBrowserContext> NewContextWithTracingAsync(string testName)
    {
        var context = await Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            BaseURL = E2eEnv.BaseUrl
        });
        await context.Tracing.StartAsync(new()
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true,
            Title = testName
        });
        return context;
    }

    public async Task StopTracingOnFailureAsync(IBrowserContext context, string testName, bool failed)
    {
        var path = Path.Combine(E2eEnv.ArtifactsDir, $"{Sanitize(testName)}.zip");
        await context.Tracing.StopAsync(new()
        {
            Path = failed ? path : null
        });
    }

    public async Task ScreenshotOnFailureAsync(IPage page, string testName, bool failed)
    {
        if (!failed) return;
        var path = Path.Combine(E2eEnv.ArtifactsDir, $"{Sanitize(testName)}.png");
        await page.ScreenshotAsync(new() { Path = path, FullPage = true });
    }

    public async Task LoginAsync(IPage page, string username, string password)
    {
        await page.GotoAsync(E2eEnv.BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (!E2eEnv.IsKeycloakLoginUrl(page.Url))
            return;

        await page.FillAsync("input[name='username']", username);
        await page.FillAsync("input[name='password']", password);
        await page.ClickAsync("input[type='submit'], button[type='submit']");
        await page.WaitForURLAsync(
            u => E2eEnv.IsGatewayUrl(u) || !E2eEnv.IsKeycloakLoginUrl(u),
            new() { Timeout = 30_000 });
        Assert.False(
            page.Url.Contains("error", StringComparison.OrdinalIgnoreCase),
            $"Login appears to have failed for user {username}; url={page.Url}");
    }

    private static string Sanitize(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
}

[CollectionDefinition("Playwright")]
public class PlaywrightCollection : ICollectionFixture<PlaywrightFixture> { }
