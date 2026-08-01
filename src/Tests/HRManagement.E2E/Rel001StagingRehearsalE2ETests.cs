using Microsoft.Playwright;

namespace HRManagement.E2E;

/// <summary>REL-001-E: staging surfaces remain auth-challenged (rehearsal smoke).</summary>
[Collection("Playwright")]
public class Rel001StagingRehearsalE2ETests
{
    private readonly PlaywrightFixture _fx;
    public Rel001StagingRehearsalE2ETests(PlaywrightFixture fx) => _fx = fx;

    [Trait("Category", "Journey")]
    [Trait("Card", "REL-001")]
    [SkippableTheory]
    [InlineData("/health/live", "REL-H1")]
    [InlineData("/ui/hr-core", "REL-F1")]
    [InlineData("/ui/auth-directory", "REL-F2")]
    public async Task Staging_Surfaces_Respond_Or_Challenge(string path, string caseId)
    {
        E2eEnv.RequireEnabled();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var api = (await browser.NewContextAsync()).APIRequest;
        await E2eEnv.EnsureGatewayReachableAsync(api);
        var response = await api.GetAsync($"{E2eEnv.BaseUrl}{path}", new() { MaxRedirects = 0 });
        if (path.StartsWith("/health", StringComparison.Ordinal))
        {
            Assert.True(response.Status is >= 200 and < 500, $"{caseId}: health status {response.Status}");
            return;
        }
        E2eHttp.EnsureAuthChallengeOrForbidden(response, caseId);
    }
}
