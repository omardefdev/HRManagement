using Microsoft.Playwright;

namespace HRManagement.E2E;

/// <summary>FUN-005-E: deepen IDCard / AdminLogistics / ITInfra surface smoke.</summary>
[Collection("Playwright")]
public class Fun005IdentityAssetsItE2ETests
{
    private readonly PlaywrightFixture _fx;
    public Fun005IdentityAssetsItE2ETests(PlaywrightFixture fx) => _fx = fx;

    [Trait("Category", "Journey")]
    [SkippableTheory]
    [InlineData("/ui/idcard/CardRequests", "ID-F1")]
    [InlineData("/ui/idcard/IssuedCards", "ID-F2")]
    [InlineData("/ui/admin-logistics/Assets", "AL-F1")]
    [InlineData("/ui/it-infra/SupportTickets", "IT-F1")]
    [InlineData("/ui/it-infra/SupportTickets/FailedProvisioning", "IT-F2")]
    [InlineData("/ui/it-infra/UserDevices", "IT-F3")]
    public async Task Anonymous_Identity_Surfaces_Are_Challenged(string path, string caseId)
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
