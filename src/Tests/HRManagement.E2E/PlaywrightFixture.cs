using System.Text.RegularExpressions;
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

    /// <summary>
    /// Playwright's own timeout did not end a screenshot of a page whose main thread was stuck (locally it waited over
    /// 3 minutes); the trace stop and the context close that follow return at once.
    /// </summary>
    private static readonly TimeSpan ScreenshotTimeout = TimeSpan.FromSeconds(15);

    public async Task ScreenshotOnFailureAsync(IPage page, string testName, bool failed)
    {
        if (!failed) return;
        var path = Path.Combine(E2eEnv.ArtifactsDir, $"{Sanitize(testName)}.png");
        try
        {
            await page.ScreenshotAsync(new() { Path = path, FullPage = true }).WaitAsync(ScreenshotTimeout);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // A crashed, closed or stuck page has no screenshot; the trace is still saved and the test's own failure reported.
        }
    }

    /// <summary>
    /// Runs a browser test on a new traced context and page. A failing test leaves {testName}.png (full page) and
    /// {testName}.zip (the Playwright trace, opened with playwright.ps1 show-trace) in E2eEnv.ArtifactsDir, which the E2E
    /// job of ci.yml uploads as e2e-traces; a passing test leaves nothing (PLAN-35 defect 9).
    /// </summary>
    public async Task RunTracedAsync(string testName, Func<IBrowserContext, IPage, Task> test)
    {
        await using var context = await NewContextWithTracingAsync(testName);
        var page = await context.NewPageAsync();
        var failed = true;
        try
        {
            await test(context, page);
            failed = false;
        }
        finally
        {
            await ScreenshotOnFailureAsync(page, testName, failed);
            await StopTracingOnFailureAsync(context, testName, failed);
        }
    }

    /// <summary>From submitting the Keycloak form to a gateway page; CI personas take 2-7 s. Known stops fail at once.</summary>
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(30);

    private static readonly Regex RequiredActionUrl = new(
        @"/login-actions/required-action\?(?:[^#]*&)?execution=([^&#]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Signs in through Keycloak and returns on a gateway page. Fails at once, naming the step, when Keycloak refuses the
    /// credentials, stops on a required action (a user imported without first and last name gets VERIFY_PROFILE, "Update
    /// Account Information") or the gateway's OIDC callback fails (a user without the offline_access role: "Offline tokens
    /// not allowed" in the gateway log). PLAN-35 defect 16: the old 60 s wait for the gateway timed out on such a stop, was
    /// swallowed, and the test went on anonymous.
    /// </summary>
    public async Task LoginAsync(IPage page, string username, string password)
    {
        await page.GotoAsync(E2eEnv.BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (E2eEnv.IsGatewayPageUrl(page.Url))
            return; // this context is already signed in
        Assert.True(E2eEnv.IsKeycloakLoginUrl(page.Url), $"Login for {username}: expected the Keycloak login page, got {page.Url}");

        // A good callback redirects to the gateway; a failed one answers 4xx/5xx (Development) or, through the error
        // page's challenge, redirects back to Keycloak (other environments).
        string? callbackFailure = null;
        void OnResponse(object? sender, IResponse response)
        {
            if (!E2eEnv.IsGatewayOidcCallbackUrl(response.Url))
                return;
            var location = response.Headers.GetValueOrDefault("location") ?? "";
            if (response.Status >= 400 || E2eEnv.IsKeycloakLoginUrl(location))
                callbackFailure = location.Length > 0 ? $"HTTP {response.Status} -> {location}" : $"HTTP {response.Status}";
        }

        page.Response += OnResponse;
        try
        {
            await page.FillAsync("input[name='username']", username);
            await page.FillAsync("input[name='password']", password);
            await page.ClickAsync("input[type='submit'], button[type='submit']");

            var deadline = DateTime.UtcNow + LoginTimeout;
            while (!E2eEnv.IsGatewayPageUrl(page.Url))
            {
                if (callbackFailure is not null)
                    Assert.Fail($"Login for {username}: the gateway's OIDC callback failed ({callbackFailure}); the gateway log has the cause.");
                if (RequiredActionUrl.Match(page.Url) is { Success: true } requiredAction)
                    Assert.Fail(
                        $"Login for {username} stopped on Keycloak required action {requiredAction.Groups[1].Value} " +
                        $"(\"{await KeycloakTextAsync(page, "#kc-page-title")}\"): the realm import leaves the user incomplete.");
                if (await KeycloakTextAsync(page, "#input-error, #kc-error-message, .alert-error .kc-feedback-text") is { Length: > 0 } error)
                    Assert.Fail($"Login for {username} refused by Keycloak: {error} (url={page.Url})");
                if (DateTime.UtcNow >= deadline)
                    Assert.Fail($"Login for {username} did not reach the gateway within {LoginTimeout.TotalSeconds:0} s; url={page.Url}");
                await Task.Delay(100);
            }
        }
        finally
        {
            page.Response -= OnResponse;
        }
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    /// <summary>Text of the first element matching the selector on the current page, or null (also while it navigates).</summary>
    private static async Task<string?> KeycloakTextAsync(IPage page, string selector)
    {
        try
        {
            var element = page.Locator(selector).First;
            return await element.CountAsync() > 0 ? (await element.InnerTextAsync(new() { Timeout = 1_000 })).Trim() : null;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static string Sanitize(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
}

[CollectionDefinition("Playwright")]
public class PlaywrightCollection : ICollectionFixture<PlaywrightFixture> { }
