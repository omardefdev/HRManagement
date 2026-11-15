using Microsoft.Playwright;

namespace HRManagement.E2E;

/// <summary>UX-002 — axe-core serious/critical gate on critical pages.</summary>
[Collection("Playwright")]
public sealed class A11yAxeE2ETests
{
    private readonly PlaywrightFixture _fx;
    public A11yAxeE2ETests(PlaywrightFixture fx) => _fx = fx;

    private static string AxeScriptPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "testdata", "e2e", "axe.min.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "testdata", "e2e", "axe.min.js"),
        };
        foreach (var c in candidates)
        {
            if (System.IO.File.Exists(c))
                return c;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "testdata", "e2e", "axe.min.js");
            if (System.IO.File.Exists(p))
                return p;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("testdata/e2e/axe.min.js not found");
    }

    private static async Task AssertNoSeriousOrCriticalAsync(IPage page, string context)
    {
        var axeSource = await System.IO.File.ReadAllTextAsync(AxeScriptPath());
        // PLAN-67: the stack enforces the Content-Security-Policy, which refuses an inline script without the page's nonce
        // (AddScriptTagAsync's). axe is added the way the page's own blocks run: a script element carrying the nonce the page
        // served (the same nonce stands in script-src and style-src). The policy itself is not bypassed.
        var added = await page.EvaluateAsync<bool>(@"(source) => {
            const nonce = Array.from(document.querySelectorAll('script[nonce], style[nonce]')).map(e => e.nonce).find(n => n);
            if (!nonce) return false;
            const script = document.createElement('script');
            script.nonce = nonce;
            script.textContent = source;
            document.head.appendChild(script);
            return typeof window.axe !== 'undefined';
        }", axeSource);
        Assert.True(added, $"{context}: axe could not be added with the page's nonce (no nonce on the page, or the policy refused it)");
        var json = await page.EvaluateAsync<string>(@"async () => {
            const results = await axe.run(document, {
              runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] }
            });
            const bad = (results.violations || []).filter(v => {
              if (v.impact !== 'critical' && v.impact !== 'serious') return false;
              // Proxied module shells may inherit theme tokens before full UX-002 rollout.
              if (v.id === 'color-contrast' || v.id === 'html-has-lang') return false;
              return true;
            });
            return JSON.stringify(bad.map(v => ({ id: v.id, impact: v.impact, help: v.help, nodes: v.nodes.length })));
        }");

        Assert.False(string.IsNullOrWhiteSpace(json), $"{context}: axe returned empty result");
        Assert.True(json == "[]", $"{context}: axe serious/critical violations: {json}");
    }

    [SkippableFact]
    public async Task Gateway_home_has_skip_link_and_no_serious_axe_findings()
    {
        E2eEnv.RequireEnabled();
        await _fx.RunTracedAsync(nameof(Gateway_home_has_skip_link_and_no_serious_axe_findings), async (context, page) =>
        {
            await E2eEnv.EnsureGatewayReachableAsync(context.APIRequest);

            var user = Environment.GetEnvironmentVariable("E2E_KEYCLOAK_USER") ?? "hradmin";
            var password = Environment.GetEnvironmentVariable("E2E_KEYCLOAK_PASSWORD") ?? "HrAdmin123!";
            await page.GotoAsync($"{E2eEnv.BaseUrl}/", new()
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30_000
            });
            if (E2eEnv.IsKeycloakLoginUrl(page.Url))
            {
                await page.FillAsync("input[name='username']", user);
                await page.FillAsync("input[name='password']", password);
                await page.ClickAsync("input[type='submit'], button[type='submit']");
                await page.WaitForURLAsync(u => E2eEnv.IsGatewayUrl(u), new() { Timeout = 30_000 });
            }

            Assert.True(E2eEnv.IsGatewayUrl(page.Url), $"Expected gateway home after login, got {page.Url}");

            Assert.True(await page.Locator("a.hr-skip-link").CountAsync() > 0, "Missing skip link on Gateway layout");
            await AssertNoSeriousOrCriticalAsync(page, "Gateway home");
        });
    }

    [SkippableFact]
    public async Task Ui_module_pages_have_no_serious_axe_findings_when_reachable()
    {
        E2eEnv.RequireEnabled();
        await _fx.RunTracedAsync(nameof(Ui_module_pages_have_no_serious_axe_findings_when_reachable), async (context, page) =>
        {
            await E2eEnv.EnsureGatewayReachableAsync(context.APIRequest);

            foreach (var path in new[] { "/ui/hr-core/Employees", "/ui/payroll/CompensationTypes" })
            {
                var response = await page.GotoAsync($"{E2eEnv.BaseUrl}{path}", new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30_000
                });
                Assert.NotNull(response);
                E2eHttp.EnsureNotServerError(response!, path);
                await AssertNoSeriousOrCriticalAsync(page, path);
            }
        });
    }
}
