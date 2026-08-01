using Microsoft.Playwright;

namespace HRManagement.E2E;

/// <summary>FUN-006-E: deepen Records.Archive / Dev.Analytics surface smoke.</summary>
[Collection("Playwright")]
public class Fun006ArchiveAnalyticsE2ETests
{
    private readonly PlaywrightFixture _fx;
    public Fun006ArchiveAnalyticsE2ETests(PlaywrightFixture fx) => _fx = fx;

    [Trait("Category", "Journey")]
    [SkippableTheory]
    [InlineData("/ui/records-archive/ArchiveItems", "AR-F1")]
    [InlineData("/ui/dev-analytics/Reports", "AN-F1")]
    [InlineData("/ui/dev-analytics/ReadModels", "AN-F2")]
    public async Task Anonymous_Archive_Analytics_Surfaces_Are_Challenged(string path, string caseId)
    {
        E2eEnv.RequireEnabled();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var api = (await browser.NewContextAsync()).APIRequest;
        await E2eEnv.EnsureGatewayReachableAsync(api);
        var response = await api.GetAsync($"{E2eEnv.BaseUrl}{path}", new() { MaxRedirects = 0 });
        E2eHttp.EnsureAuthChallengeOrForbidden(response, caseId);
    }
}
