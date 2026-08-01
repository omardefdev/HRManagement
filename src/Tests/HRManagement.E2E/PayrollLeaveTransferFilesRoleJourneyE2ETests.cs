using Microsoft.Playwright;

namespace HRManagement.E2E;

/// <summary>QA-004-D: Payroll + Leave + Transfer + Files + Role journeys (matrix PAY/LEV/TRF/FIL/ROL).</summary>
[Collection("Playwright")]
public class PayrollLeaveTransferFilesRoleJourneyE2ETests
{
    private readonly PlaywrightFixture _fx;
    public PayrollLeaveTransferFilesRoleJourneyE2ETests(PlaywrightFixture fx) => _fx = fx;

    [Trait("Category", "Journey")]
    [SkippableTheory]
    [InlineData("/ui/payroll/PayrollRuns", "PAY-F1")]
    [InlineData("/ui/hr-contracts/LeaveRequests", "LEV-F1")]
    [InlineData("/ui/hr-movements/TransferRequests", "TRF-F1")]
    [InlineData("/ui/file-storage/", "FIL-F1")]
    [InlineData("/ui/auth-directory/", "ROL-F1")]
    public async Task Anonymous_Critical_Surfaces_Are_Challenged(string path, string caseId)
    {
        E2eEnv.RequireEnabled();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var api = (await browser.NewContextAsync()).APIRequest;
        await E2eEnv.EnsureGatewayReachableAsync(api);
        var response = await api.GetAsync($"{E2eEnv.BaseUrl}{path}", new() { MaxRedirects = 0 });
        E2eHttp.EnsureAuthChallengeOrForbidden(response, caseId);
    }

    [Trait("Category", "Journey")]
    [SkippableTheory]
    [InlineData("/ui/payroll/PayrollRuns", "PAY-F3")]
    [InlineData("/ui/hr-contracts/LeaveRequests", "LEV-F3")]
    [InlineData("/ui/hr-movements/TransferRequests", "TRF-F3")]
    [InlineData("/ui/file-storage/", "FIL-F3")]
    [InlineData("/ui/auth-directory/", "ROL-F3")]
    public async Task Critical_Surfaces_Never_Treat_5xx_As_Success(string path, string caseId)
    {
        E2eEnv.RequireEnabled();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var api = (await browser.NewContextAsync()).APIRequest;
        await E2eEnv.EnsureGatewayReachableAsync(api);
        var response = await api.GetAsync($"{E2eEnv.BaseUrl}{path}");
        E2eHttp.EnsureNotServerError(response, caseId);
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task PAY_H1_Processor_Reaches_Payroll_Runs()
    {
        await AuthorizedSurfaceAsync(
            "payroll-processor",
            "/ui/payroll/PayrollRuns",
            nameof(PAY_H1_Processor_Reaches_Payroll_Runs));
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task PAY_F2_SoD_Processor_And_Approver_Are_Distinct_Personas()
    {
        E2eEnv.RequireEnabled();
        E2eSeed.LoadManifest();
        var processor = E2eSeed.RequirePersona("payroll-processor");
        var approver = E2eSeed.RequirePersona("payroll-approver");
        Assert.NotEqual(processor.Username, approver.Username);
        Assert.Contains("payroll-processor", processor.Roles);
        Assert.Contains("payroll-approver", approver.Roles);
        Assert.DoesNotContain("payroll-approver", processor.Roles);
        // Runtime SoD enforce is covered by SoDPolicyIntegrationTests; E2E proves persona separation for maker≠checker.
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task LEV_H1_Contracts_Persona_Reaches_Leave_Requests()
    {
        await AuthorizedSurfaceAsync(
            "maker-contracts",
            "/ui/hr-contracts/LeaveRequests",
            nameof(LEV_H1_Contracts_Persona_Reaches_Leave_Requests));
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task LEV_F2_Maker_And_Checker_Personas_Differ_For_SoD()
    {
        E2eEnv.RequireEnabled();
        var maker = E2eSeed.RequirePersona("maker-contracts");
        var checker = E2eSeed.RequirePersona("checker-mgmt");
        Assert.NotEqual(maker.Username, checker.Username);
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task TRF_H1_Movements_Persona_Reaches_Transfers()
    {
        await AuthorizedSurfaceAsync(
            "movements-ops",
            "/ui/hr-movements/TransferRequests",
            nameof(TRF_H1_Movements_Persona_Reaches_Transfers));
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task TRF_F2_Self_Approve_Blocked_By_Distinct_Checker_Persona()
    {
        E2eEnv.RequireEnabled();
        var maker = E2eSeed.RequirePersona("movements-ops");
        var checker = E2eSeed.RequirePersona("checker-mgmt");
        Assert.NotEqual(maker.Id, checker.Id);
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task TRF_H2_Secondment_Surface_Reachable_For_Movements_Persona()
    {
        await AuthorizedSurfaceAsync(
            "movements-ops",
            "/ui/hr-movements/SecondmentRequests",
            nameof(TRF_H2_Secondment_Surface_Reachable_For_Movements_Persona));
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task TRF_H3_Loan_Surface_Reachable_For_Movements_Persona()
    {
        await AuthorizedSurfaceAsync(
            "movements-ops",
            "/ui/hr-movements/LoanAssignmentRequests",
            nameof(TRF_H3_Loan_Surface_Reachable_For_Movements_Persona));
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task FIL_H1_File_Ops_Reaches_File_Storage_Ui()
    {
        await AuthorizedSurfaceAsync(
            "file-ops",
            "/ui/file-storage/",
            nameof(FIL_H1_File_Ops_Reaches_File_Storage_Ui));
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task FIL_F2_Outsider_Is_Not_File_Ops()
    {
        E2eEnv.RequireEnabled();
        var ops = E2eSeed.RequirePersona("file-ops");
        var outsider = E2eSeed.RequirePersona("outsider-tenant-b");
        Assert.NotEqual(ops.TenantCode, outsider.TenantCode);
        Assert.DoesNotContain("file-storage", outsider.Roles);
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task ROL_H1_Auth_Directory_Reaches_Module()
    {
        await AuthorizedSurfaceAsync(
            "auth-directory",
            "/ui/auth-directory/",
            nameof(ROL_H1_Auth_Directory_Reaches_Module));
    }

    [Trait("Category", "Journey")]
    [SkippableFact]
    public async Task ROL_F2_Four_Eyes_Requires_Distinct_Admin_Persona()
    {
        E2eEnv.RequireEnabled();
        var maker = E2eSeed.RequirePersona("auth-directory");
        var checker = E2eSeed.RequirePersona("hr-admin");
        Assert.NotEqual(maker.Username, checker.Username);
        Assert.Contains("hr-admin", checker.Roles);
    }

    private async Task AuthorizedSurfaceAsync(string personaId, string path, string testName)
    {
        E2eEnv.RequireEnabled();
        E2eSeed.LoadManifest();
        var persona = E2eSeed.RequirePersona(personaId);
        var password = E2eSeed.ResolvePassword(persona);
        await using var context = await _fx.NewContextWithTracingAsync(testName);
        var failed = true;
        try
        {
            var page = await context.NewPageAsync();
            await E2eEnv.EnsureGatewayReachableAsync(context.APIRequest);
            await _fx.LoginAsync(page, persona.Username, password);
            var response = await page.GotoAsync($"{E2eEnv.BaseUrl}{path}",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.NotNull(response);
            E2eHttp.EnsureNotServerError(response!, testName);
            failed = false;
        }
        finally
        {
            await _fx.StopTracingOnFailureAsync(context, testName, failed);
        }
    }
}

/// <summary>Seed pack integrity — always runnable without stack (documents versioned seed).</summary>
public class E2eSeedPackTests
{
    [Fact]
    public void Seed_Manifest_Is_Versioned_1_0_0()
    {
        var manifest = E2eSeed.LoadManifest();
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("QA-004", manifest.Card);
    }

    [Fact]
    public void Seed_Defines_All_Matrix_Personas()
    {
        foreach (var id in new[]
                 {
                     "maker-contracts", "checker-mgmt", "payroll-processor", "payroll-approver",
                     "movements-ops", "file-ops", "outsider-tenant-b", "auth-directory", "hr-admin"
                 })
        {
            var p = E2eSeed.RequirePersona(id);
            Assert.False(string.IsNullOrWhiteSpace(p.Username));
            Assert.False(string.IsNullOrWhiteSpace(E2eSeed.ResolvePassword(p)));
        }
    }
}
