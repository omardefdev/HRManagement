using HRManagement.E2E;
using Microsoft.Playwright;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-67 on the demo server (<c>VPS_SSH_HOST=hrm-vps</c>), with the AC-OPS-36 helpers. Opens the key pages (P-12) in Chromium,
/// signed in as <c>hradmin</c> (the portal anonymous), counts the Content-Security-Policy policies of each response and records
/// every <c>securitypolicyviolation</c> event, and takes screenshots at 1366×768 and 390×844. With
/// <c>UX067_VPS_LABEL=before</c> it only records (P0, the baseline); otherwise every page must carry one policy and raise no
/// violation. The demo users' password is read into memory only; the session state lives in a temporary file deleted at the end;
/// evidence goes under <c>artifacts/plan67/vps/&lt;label&gt;</c> and is never committed.
/// </summary>
public partial class AcOps36VpsBootTests
{
    private static string Ux67Label => Environment.GetEnvironmentVariable("UX067_VPS_LABEL") is { Length: > 0 } label ? label : "after";

    private static EvidenceRecorder StartUx67(string key, string scenario)
    {
        var recorder = new EvidenceRecorder("vps", $"ux67-{key}-{Guid.NewGuid():N}", Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan67", "vps", Ux67Label))
        {
            ScenarioKey = key
        };
        recorder.WriteMetadata(scenario, "hradmin on the demo server (password read into memory only); the portal anonymous");
        AcceptanceGate.RequireEnabled(recorder);
        AcceptanceGate.RequireData(recorder, Remote, "خادم العرض غير محدد", "Set VPS_SSH_HOST=hrm-vps: this check is for the demo server.");
        return recorder;
    }

    [SkippableFact]
    public async Task AC_UX_67_vps_every_key_page_carries_one_policy_and_raises_no_violation()
    {
        var recorder = StartUx67("csp", "Demo server: the key pages, one CSP policy each and no securitypolicyviolation, with screenshots at two sizes");
        var outDir = Path.Combine(AcceptanceHarness.RepoRoot, "artifacts", "plan67", "vps", Ux67Label);
        var statePath = Path.Combine(Path.GetTempPath(), $"ux67-state-{Guid.NewGuid():N}.json");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        try
        {
            var baseUrl = $"https://{AppDomain}";
            await Ux067PageProbe.SignInAndSaveStateAsync(browser, baseUrl, "hradmin", DemoPassword(), statePath);
            var results = await Ux067PageProbe.ProbeAsync(
                browser, baseUrl, statePath, Ux067PageProbe.KeyPages, [(1366, 768), (390, 844)], Path.Combine(outDir, "screenshots"));
            await File.WriteAllTextAsync(Path.Combine(outDir, "csp-pages.json"), Ux067PageProbe.ToJson(results));

            recorder.WriteScenarioSteps(string.Join("\n", results.Select(r =>
                $"- {r.Name}: HTTP {r.Status}, {r.CspHeaders} CSP header(s), {r.CspPolicies} policies, {r.Violations.Count} violation(s)" +
                (r.Note is null ? "" : $" ({r.Note})"))));

            if (string.Equals(Ux67Label, "before", StringComparison.OrdinalIgnoreCase))
            {
                recorder.WriteResult("Pass", "baseline recorded (no assertion in the before run)");
                return;
            }

            var failures = results
                .Where(r => r.Note is not null || r.CspPolicies != 1 || r.Violations.Count > 0)
                .Select(r => $"{r.Name}: {r.CspPolicies} policies, {r.Violations.Count} violations {string.Join("; ", r.Violations.Select(v => $"{v.Directive} {v.BlockedUri} {v.Source}"))} {r.Note}")
                .ToList();
            Assert.True(failures.Count == 0, "Key pages failing the CSP check:\n" + string.Join("\n", failures));
            recorder.WriteResult("Pass");
        }
        finally
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }
}
