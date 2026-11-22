using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HRManagement.E2E;

/// <summary>One CSP violation the browser raised on a page (the <c>securitypolicyviolation</c> event).</summary>
public sealed record Ux067Violation(string Directive, string BlockedUri, string Sample, string Source);

/// <summary>What one key page showed: its CSP policies, its violations, and the screenshots taken.</summary>
public sealed record Ux067PageResult(
    string Name,
    string Requested,
    string FinalUrl,
    int Status,
    int CspHeaders,
    int CspPolicies,
    int ReportOnlyPolicies,
    IReadOnlyList<Ux067Violation> Violations,
    IReadOnlyList<string> Screenshots,
    string? Note,
    bool Resolved = true,
    bool Optional = false)
{
    /// <summary>A page that resolved and carried one policy with no violation, or an optional page this stack has no record for.</summary>
    public bool Passes => Resolved ? Note is null && CspPolicies == 1 && Violations.Count == 0 : Optional;
}

/// <summary>
/// A key page of PLAN-67 (P-12): a fixed path, or a page found by following the first link of a list page whose href matches
/// <see cref="LinkPattern"/>; <see cref="Anonymous"/> pages are opened without a session (the public portal).
/// </summary>
public sealed record Ux067KeyPage(string Name, string Path, string? LinkPattern = null, bool Anonymous = false, string? DetailsPattern = null, bool Optional = false);

/// <summary>
/// PLAN-67 (G-UX-01, G-UX-02): opens pages in Chromium and records, per page, how many Content-Security-Policy policies the
/// response carried and every <c>securitypolicyviolation</c> event the page raised. The listener is an init script, so it is
/// registered before the page's own markup is parsed (an inline style attribute or a wrongly-nonced block is reported while
/// the document loads). Screenshots are taken at the sizes the caller gives, with deviceScaleFactor 1.
/// </summary>
public static class Ux067PageProbe
{
    public const string ViolationsGlobal = "__hrCspViolations";

    public static readonly string ViolationInitScript =
        "(() => { window." + ViolationsGlobal + " = []; document.addEventListener('securitypolicyviolation', function (e) { " +
        "window." + ViolationsGlobal + ".push({ directive: e.effectiveDirective || e.violatedDirective || '', blocked: e.blockedURI || '', " +
        "sample: (e.sample || '').substring(0, 120), source: (e.sourceFile || '') + ':' + (e.lineNumber || 0) }); }, true); })();";

    /// <summary>The PLAN-67 key pages (P-12) on the gateway; ids are found by following list links.</summary>
    public static IReadOnlyList<Ux067KeyPage> KeyPages { get; } =
    [
        new("careers-index", "/careers", Anonymous: true),
        new("careers-posting", "/careers", @"^(?:https?://[^/]+)?/careers/(?:careers/)?[0-9a-fA-F-]{36}/?$", Anonymous: true),
        new("careers-track", "/careers/track", Anonymous: true),
        new("gateway-home", "/"),
        new("hr-core-root", "/ui/hr-core/"),
        new("hr-core-dashboard", "/ui/hr-core/Dashboard"),
        new("employees", "/ui/hr-core/Employees"),
        new("employee-details", "/ui/hr-core/Employees", @"/Employees/Details/[0-9a-fA-F-]{36}"),
        new("work-inbox", "/ui/hr-core/WorkInbox"),
        new("approval-review", "/ui/hr-core/Approvals", @"/Approvals/Review/[0-9a-fA-F-]{36}"),
        new("requisition-details", "/ui/hr-core/RecruitmentRequisitions", @"/RecruitmentRequisitions/Details/[0-9a-fA-F-]{36}"),
        // Optional: neither the prod-like seed nor the demo holds an application with an accepted offer, the only one the page opens for.
        new("hire", "/ui/hr-core/RecruitmentApplications", @"/RecruitmentApplications/Hire/[0-9a-fA-F-]{36}",
            DetailsPattern: @"/RecruitmentApplications/Details/[0-9a-fA-F-]{36}", Optional: true),
        new("policy-index", "/ui/hr-core/PolicyTemplates"),
        new("policy-details", "/ui/hr-core/PolicyTemplates", @"/PolicyTemplates/Details/[0-9a-fA-F-]{36}"),
        new("policy-rules", "/ui/hr-core/PolicyTemplates", @"/PolicyTemplates/EditRules\?versionId=[0-9a-fA-F-]{36}",
            DetailsPattern: @"/PolicyTemplates/Details/[0-9a-fA-F-]{36}"),
        new("policy-simulate", "/ui/hr-core/PolicyTemplates", @"/PolicyTemplates/Simulate\?versionId=[0-9a-fA-F-]{36}",
            DetailsPattern: @"/PolicyTemplates/Details/[0-9a-fA-F-]{36}"),
        new("establishment-change", "/ui/hr-core/EstablishmentChanges", @"/EstablishmentChanges/Details/[0-9a-fA-F-]{36}"),
        new("funded-position", "/ui/hr-core/FundedPositions", @"/FundedPositions/Details/[0-9a-fA-F-]{36}"),
        new("contracts-hire-wizard", "/ui/hr-contracts/HireWizard"),
        new("payroll-dashboard", "/ui/payroll/Dashboard"),
        // PLAN-53: the salary change and reference change screens (read pages of the payroll module; the lists may be empty).
        new("payroll-salary-changes", "/ui/payroll/PayProfileChanges"),
        new("payroll-reference-changes", "/ui/payroll/PayrollReferenceChanges"),
        new("payroll-grade-details", "/ui/payroll/PayGrades", @"/PayGrades/Details/[0-9a-fA-F-]{36}"),
        new("asset-details", "/ui/admin-logistics/Assets", @"/Assets/Details/[0-9a-fA-F-]{36}"),
        new("transfers", "/ui/hr-movements/TransferRequests"),
    ];

    public static async Task<IBrowserContext> NewContextAsync(IBrowser browser, string baseUrl, int width, int height, string? storageStatePath = null)
    {
        var context = await browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            BaseURL = baseUrl,
            ViewportSize = new() { Width = width, Height = height },
            DeviceScaleFactor = 1,
            StorageStatePath = storageStatePath
        });
        await context.AddInitScriptAsync(ViolationInitScript);
        return context;
    }

    /// <summary>The number of policies in the CSP headers of a response: one per header, more when a header joins policies with commas.</summary>
    public static (int Headers, int Policies, int ReportOnly) CountPolicies(IEnumerable<KeyValuePair<string, string>> headers)
    {
        int headerCount = 0, policies = 0, reportOnly = 0;
        foreach (var (name, value) in headers)
        {
            var count = value.Split(',').Count(p => p.Contains("-src", StringComparison.OrdinalIgnoreCase) || p.Contains("default-src", StringComparison.OrdinalIgnoreCase));
            if (string.Equals(name, "content-security-policy", StringComparison.OrdinalIgnoreCase))
            {
                headerCount++;
                policies += Math.Max(count, 1);
            }
            else if (string.Equals(name, "content-security-policy-report-only", StringComparison.OrdinalIgnoreCase))
            {
                reportOnly += Math.Max(count, 1);
            }
        }

        return (headerCount, policies, reportOnly);
    }

    public static async Task<IReadOnlyList<Ux067Violation>> ReadViolationsAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>($"() => JSON.stringify(window.{ViolationsGlobal} || [])");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(e => new Ux067Violation(
                e.GetProperty("directive").GetString() ?? "",
                e.GetProperty("blocked").GetString() ?? "",
                e.GetProperty("sample").GetString() ?? "",
                e.GetProperty("source").GetString() ?? ""))
            .ToList();
    }

    /// <summary>Opens a page, waits for it to settle, and returns its main response's CSP counts and its violations.</summary>
    public static async Task<(int Status, int Headers, int Policies, int ReportOnly, IReadOnlyList<Ux067Violation> Violations)> OpenAsync(IPage page, string path)
    {
        var response = await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // A page that keeps a connection open (live notifications) never goes idle; its load event is enough.
        }

        var headers = response is null ? [] : (await response.HeadersArrayAsync()).Select(h => new KeyValuePair<string, string>(h.Name, h.Value));
        var (count, policies, reportOnly) = CountPolicies(headers);
        return (response?.Status ?? 0, count, policies, reportOnly, await ReadViolationsAsync(page));
    }

    /// <summary>The first href on the current page matching the pattern, made absolute against the page.</summary>
    public static async Task<string?> FindLinkAsync(IPage page, string pattern)
    {
        var hrefs = await page.EvalOnSelectorAllAsync<string[]>("a[href]", "els => els.map(e => e.href)");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return hrefs.FirstOrDefault(h => regex.IsMatch(new Uri(h).PathAndQuery) || regex.IsMatch(h));
    }

    /// <summary>
    /// Resolves a key page to a URL: its path, or the first matching link of its list page, or (with
    /// <see cref="Ux067KeyPage.DetailsPattern"/>) the first matching link on one of the first details pages of that list.
    /// </summary>
    public static async Task<(string? Url, string? Note)> ResolveAsync(IPage page, Ux067KeyPage key)
    {
        if (key.LinkPattern is null)
            return (key.Path, null);

        await page.GotoAsync(key.Path, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
        if (await FindLinkAsync(page, key.LinkPattern) is { } direct)
            return (direct, null);
        if (key.DetailsPattern is null)
            return (null, $"no link matching {key.LinkPattern} on {key.Path}");

        var hrefs = await page.EvalOnSelectorAllAsync<string[]>("a[href]", "els => els.map(e => e.href)");
        var details = new Regex(key.DetailsPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var candidate in hrefs.Where(h => details.IsMatch(h)).Distinct().Take(15))
        {
            await page.GotoAsync(candidate, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
            if (await FindLinkAsync(page, key.LinkPattern) is { } found)
                return (found, null);
        }

        return (null, $"no link matching {key.LinkPattern} on {key.Path} or its first details pages");
    }

    /// <summary>
    /// Probes every key page at each size: opens it in a fresh page of a context of that size (signed in through the storage
    /// state, or anonymous), records the CSP counts and violations, and saves a screenshot to <paramref name="screenshotDir"/>.
    /// </summary>
    public static async Task<IReadOnlyList<Ux067PageResult>> ProbeAsync(
        IBrowser browser,
        string baseUrl,
        string? signedInStatePath,
        IEnumerable<Ux067KeyPage> pages,
        IReadOnlyList<(int Width, int Height)> sizes,
        string screenshotDir)
    {
        Directory.CreateDirectory(screenshotDir);
        var results = new List<Ux067PageResult>();
        var resolved = new Dictionary<string, (string? Url, string? Note)>();
        foreach (var key in pages)
        {
            var shots = new List<string>();
            var violations = new List<Ux067Violation>();
            int status = 0, headers = 0, policies = 0, reportOnly = 0;
            string finalUrl = "";
            string? note = null;
            foreach (var (width, height) in sizes)
            {
                await using var context = await NewContextAsync(browser, baseUrl, width, height, key.Anonymous ? null : signedInStatePath);
                var page = await context.NewPageAsync();
                if (!resolved.TryGetValue(key.Name, out var target))
                {
                    target = await ResolveAsync(page, key);
                    resolved[key.Name] = target;
                }

                if (target.Url is null)
                {
                    note = target.Note;
                    break;
                }

                var opened = await OpenAsync(page, target.Url);
                status = opened.Status;
                headers = Math.Max(headers, opened.Headers);
                policies = Math.Max(policies, opened.Policies);
                reportOnly = Math.Max(reportOnly, opened.ReportOnly);
                finalUrl = page.Url;
                foreach (var v in opened.Violations)
                    if (!violations.Contains(v))
                        violations.Add(v);

                var file = Path.Combine(screenshotDir, $"{key.Name}-{width}x{height}.png");
                try
                {
                    await page.ScreenshotAsync(new() { Path = file, FullPage = true }).WaitAsync(TimeSpan.FromSeconds(20));
                    shots.Add(file);
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    note = $"screenshot failed at {width}x{height}: {ex.Message.Split('\n')[0]}";
                }
            }

            results.Add(new Ux067PageResult(key.Name, resolved[key.Name].Url ?? key.Path, finalUrl, status, headers, policies, reportOnly, violations, shots, note,
                Resolved: resolved[key.Name].Url is not null, Optional: key.Optional));
        }

        return results;
    }

    /// <summary>Signs in once through Keycloak in a context and saves its storage state for the probe's contexts.</summary>
    public static async Task<string> SignInAndSaveStateAsync(IBrowser browser, string baseUrl, string username, string password, string statePath)
    {
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = baseUrl });
        var page = await context.NewPageAsync();
        await page.GotoAsync(baseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
        if (page.Url.Contains("/protocol/openid-connect/", StringComparison.OrdinalIgnoreCase) || page.Url.Contains("/login-actions/", StringComparison.OrdinalIgnoreCase))
        {
            await page.FillAsync("input[name='username']", username);
            await page.FillAsync("input[name='password']", password);
            await page.ClickAsync("input[type='submit'], button[type='submit']");
            var deadline = DateTime.UtcNow.AddSeconds(45);
            var gateway = new Uri(baseUrl).Authority;
            while (!new Uri(page.Url).Authority.Equals(gateway, StringComparison.OrdinalIgnoreCase)
                   || page.Url.Contains("/signin-oidc", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.UtcNow > deadline)
                    throw new InvalidOperationException($"Sign-in of {username} did not return to the gateway (at {new Uri(page.Url).Authority}{new Uri(page.Url).AbsolutePath}).");
                await Task.Delay(200);
            }

            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        await context.StorageStateAsync(new() { Path = statePath });
        return statePath;
    }

    public static string ToJson(IReadOnlyList<Ux067PageResult> results) =>
        JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
