using HRManagement.E2E;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-67 (UX-A) on the prod-like stack, which enforces the CSP as Production does. The baseline recorder (P0, 0.5) opens the key
/// pages (P-12) as <c>a-directory-checker</c> (hr-admin, agency A: the demo's <c>hradmin</c>), the portal anonymous, and records each
/// page's CSP policies, its violations and its screenshots at 1366×768 and 390×844; it runs only with <c>UX067_BASELINE_OUT</c> set.
/// </summary>
public class AcUx67ReadableInterfaceTests
{
    [SkippableFact]
    public async Task Records_the_key_pages_baseline_when_asked()
    {
        var outDir = Environment.GetEnvironmentVariable("UX067_BASELINE_OUT");
        Skip.If(string.IsNullOrWhiteSpace(outDir), "Set UX067_BASELINE_OUT to record the key pages of the local stack (PLAN-67 P0).");

        var persona = AcceptanceStack.Persona("a-directory-checker");
        var baseUrl = (Environment.GetEnvironmentVariable("GATEWAY_BASE_URL") ?? "http://localhost:6200").TrimEnd('/');
        var statePath = Path.Combine(Path.GetTempPath(), $"ux67-local-{Guid.NewGuid():N}.json");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        try
        {
            await Ux067PageProbe.SignInAndSaveStateAsync(browser, baseUrl, persona.Username, AcceptanceStack.Password(persona), statePath);
            var results = await Ux067PageProbe.ProbeAsync(
                browser, baseUrl, statePath, Ux067PageProbe.KeyPages, [(1366, 768), (390, 844)], Path.Combine(outDir, "screenshots"));
            await File.WriteAllTextAsync(Path.Combine(outDir, "csp-pages.json"), Ux067PageProbe.ToJson(results));
        }
        finally
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }
}
