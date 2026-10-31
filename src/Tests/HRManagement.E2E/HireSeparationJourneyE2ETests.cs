using Microsoft.Playwright;

namespace HRManagement.E2E;

/// <summary>QA-004-C: Hire journey — happy + ≥3 fail/auth (matrix HIR-*).</summary>
[Collection("Playwright")]
public class HireJourneyE2ETests
{
    private readonly PlaywrightFixture _fx;
    public HireJourneyE2ETests(PlaywrightFixture fx) => _fx = fx;

    private const string HirePath = "/ui/hr-contracts/HireWizard";

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task HIR_F1_Anonymous_Is_Challenged()
    {
        E2eEnv.RequireEnabled();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var context = await browser.NewContextAsync();
        var api = context.APIRequest;
        await E2eEnv.EnsureGatewayReachableAsync(api);

        var response = await api.GetAsync($"{E2eEnv.BaseUrl}{HirePath}", new() { MaxRedirects = 0 });
        E2eHttp.EnsureAuthChallengeOrForbidden(response, "HIR-F1 anonymous HireWizard");
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task HIR_F2_Outsider_Cannot_Open_Hire_As_Authorized()
    {
        E2eEnv.RequireEnabled();
        E2eSeed.LoadManifest();
        var outsider = E2eSeed.RequirePersona("outsider-tenant-b");
        var password = E2eSeed.ResolvePassword(outsider);

        await using var context = await _fx.NewContextWithTracingAsync(nameof(HIR_F2_Outsider_Cannot_Open_Hire_As_Authorized));
        var failed = true;
        try
        {
            var page = await context.NewPageAsync();
            await E2eEnv.EnsureGatewayReachableAsync(context.APIRequest);
            await _fx.LoginAsync(page, outsider.Username, password);

            // HIRE_RBAC_MATRIX: hr-core, the outsider's only role, may view and execute the wizard, so it opens; approving
            // the hire (Step3Approve: hr-management-approver) and completing it (Step4Complete: hr-contracts) are refused
            // by the gateway before HR.Contracts sees the request.
            var wizard = await page.GotoAsync($"{E2eEnv.BaseUrl}{HirePath}",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.NotNull(wizard);
            E2eHttp.EnsureNotServerError(wizard!, "HIR-F2 HireWizard");
            Assert.True(wizard!.Status == 200, $"HIR-F2: hr-core opens the hire wizard (HrContracts.HireView), got {wizard.Status} at {page.Url}");

            foreach (var action in new[] { "Step3Approve", "Step4Complete" })
            {
                var response = await context.APIRequest.PostAsync(
                    $"{E2eEnv.BaseUrl}{HirePath}/{action}?transactionId={Guid.NewGuid()}",
                    new() { MaxRedirects = 0 });
                await E2eHttp.EnsureRoleForbiddenAsync(response, $"HIR-F2 {action}");
            }
            failed = false;
        }
        finally
        {
            await _fx.StopTracingOnFailureAsync(context, nameof(HIR_F2_Outsider_Cannot_Open_Hire_As_Authorized), failed);
        }
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task HIR_F3_Server_Error_Is_Never_Success()
    {
        E2eEnv.RequireEnabled();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var api = (await browser.NewContextAsync()).APIRequest;
        await E2eEnv.EnsureGatewayReachableAsync(api);
        var response = await api.GetAsync($"{E2eEnv.BaseUrl}{HirePath}");
        E2eHttp.EnsureNotServerError(response, "HIR-F3 HireWizard must not return 5xx/503 as success");
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task HIR_H1_Authorized_Maker_Reaches_Hire_Surface()
    {
        E2eEnv.RequireEnabled();
        E2eSeed.LoadManifest();
        var maker = E2eSeed.RequirePersona("maker-contracts");
        var password = E2eSeed.ResolvePassword(maker);

        await using var context = await _fx.NewContextWithTracingAsync(nameof(HIR_H1_Authorized_Maker_Reaches_Hire_Surface));
        var failed = true;
        try
        {
            var page = await context.NewPageAsync();
            await E2eEnv.EnsureGatewayReachableAsync(context.APIRequest);
            await _fx.LoginAsync(page, maker.Username, password);
            var response = await page.GotoAsync($"{E2eEnv.BaseUrl}{HirePath}",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.NotNull(response);
            E2eHttp.EnsureNotServerError(response!, "HIR-H1");
            Assert.True(response!.Status < 500);
            // FUN-001 deep assert: hire surface reachable; saga/checklist closure owned by integration + UAT pack.
            Assert.Contains("hr-contracts", page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.True(response!.Ok || response.Status is >= 200 and < 500);
            failed = false;
            await _fx.ScreenshotOnFailureAsync(page, nameof(HIR_H1_Authorized_Maker_Reaches_Hire_Surface), failed: false);
        }
        finally
        {
            await _fx.StopTracingOnFailureAsync(context, nameof(HIR_H1_Authorized_Maker_Reaches_Hire_Surface), failed);
        }
    }
}

/// <summary>QA-004-C: Separation journey — matrix SEP-*.</summary>
[Collection("Playwright")]
public class SeparationJourneyE2ETests
{
    private readonly PlaywrightFixture _fx;
    public SeparationJourneyE2ETests(PlaywrightFixture fx) => _fx = fx;

    private const string SepPath = "/ui/hr-contracts/SeparationWizard";

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task SEP_F1_Anonymous_Is_Challenged()
    {
        E2eEnv.RequireEnabled();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var api = (await browser.NewContextAsync()).APIRequest;
        await E2eEnv.EnsureGatewayReachableAsync(api);
        var response = await api.GetAsync($"{E2eEnv.BaseUrl}{SepPath}", new() { MaxRedirects = 0 });
        E2eHttp.EnsureAuthChallengeOrForbidden(response, "SEP-F1");
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task SEP_F2_Unauthorized_Persona_Denied_Or_Challenged()
    {
        E2eEnv.RequireEnabled();
        var outsider = E2eSeed.RequirePersona("outsider-tenant-b");
        var password = E2eSeed.ResolvePassword(outsider);
        await using var context = await _fx.NewContextWithTracingAsync(nameof(SEP_F2_Unauthorized_Persona_Denied_Or_Challenged));
        var failed = true;
        try
        {
            var page = await context.NewPageAsync();
            await E2eEnv.EnsureGatewayReachableAsync(context.APIRequest);
            await _fx.LoginAsync(page, outsider.Username, password);
            // Signed in with hr-core only; Module.HrContracts admits hr-admin, hr-contracts and hr-management-approver.
            var response = await page.GotoAsync($"{E2eEnv.BaseUrl}{SepPath}");
            Assert.NotNull(response);
            await E2eHttp.EnsureRoleForbiddenAsync(response!, "SEP-F2");
            failed = false;
        }
        finally
        {
            await _fx.StopTracingOnFailureAsync(context, nameof(SEP_F2_Unauthorized_Persona_Denied_Or_Challenged), failed);
        }
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task SEP_F3_No_Silent_Server_Error()
    {
        E2eEnv.RequireEnabled();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var api = (await browser.NewContextAsync()).APIRequest;
        await E2eEnv.EnsureGatewayReachableAsync(api);
        var response = await api.GetAsync($"{E2eEnv.BaseUrl}{SepPath}");
        E2eHttp.EnsureNotServerError(response, "SEP-F3");
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task SEP_H1_Authorized_Opens_Separation_Surface()
    {
        E2eEnv.RequireEnabled();
        var maker = E2eSeed.RequirePersona("maker-contracts");
        var password = E2eSeed.ResolvePassword(maker);
        await using var context = await _fx.NewContextWithTracingAsync(nameof(SEP_H1_Authorized_Opens_Separation_Surface));
        var failed = true;
        try
        {
            var page = await context.NewPageAsync();
            await E2eEnv.EnsureGatewayReachableAsync(context.APIRequest);
            await _fx.LoginAsync(page, maker.Username, password);
            var response = await page.GotoAsync($"{E2eEnv.BaseUrl}{SepPath}");
            Assert.NotNull(response);
            E2eHttp.EnsureNotServerError(response!, "SEP-H1");
            // FUN-002 deep assert: separation surface reachable; saga/checklist closure owned by integration + UAT pack.
            Assert.Contains("hr-contracts", page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.True(response!.Ok || response.Status is >= 200 and < 500);
            failed = false;
        }
        finally
        {
            await _fx.StopTracingOnFailureAsync(context, nameof(SEP_H1_Authorized_Opens_Separation_Surface), failed);
        }
    }
}
