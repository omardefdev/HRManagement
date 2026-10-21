using System.Globalization;
using HRManagement.ServiceDefaults.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

public class L10n001LocalizationTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "L10N_001_LOCALIZATION.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Default_culture_is_ar_SY()
    {
        Assert.Equal("ar-SY", HrLocalizationExtensions.DefaultCultureName);
    }

    [Fact]
    public void Supported_cultures_include_ar_SY_and_en()
    {
        var names = HrLocalizationExtensions.SupportedCultures.Select(c => c.Name).ToArray();
        Assert.Contains("ar-SY", names);
        Assert.Contains("en", names);
    }

    [Fact]
    public void Cookie_name_is_defined()
    {
        Assert.False(string.IsNullOrWhiteSpace(HrLocalizationExtensions.CultureCookieName));
        Assert.Equal(".HrManagement.Culture", HrLocalizationExtensions.CultureCookieName);
    }

    [Fact]
    public void Request_culture_uses_cookie_and_query_only_not_browser_accept_language()
    {
        var services = new ServiceCollection();
        services.AddHrLocalization();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
        Assert.Equal(2, options.RequestCultureProviders.Count);
        Assert.DoesNotContain(
            options.RequestCultureProviders,
            p => p is AcceptLanguageHeaderRequestCultureProvider);
    }

    [Fact]
    public void DamascusCalendar_timezone_is_Damascus()
    {
        var svc = new DamascusCalendarService();
        var tz = svc.TimeZone;
        Assert.True(
            tz.Id == "Asia/Damascus" || tz.Id == "Syria Standard Time",
            $"Expected Damascus timezone but got {tz.Id}");
    }

    [Fact]
    public void DamascusCalendar_converts_utc_to_local()
    {
        var svc = new DamascusCalendarService();
        var utc = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var local = svc.ToLocalTime(utc);
        Assert.Equal(svc.TimeZone.GetUtcOffset(utc), local.Offset);
    }

    [Fact]
    public void DamascusCalendar_formats_gregorian_date()
    {
        var svc = new DamascusCalendarService();
        var date = new DateTime(2026, 7, 31);
        var formatted = svc.FormatGregorianDate(date, "en");
        Assert.NotNull(formatted);
        Assert.Contains("2026", formatted);
    }

    [Fact]
    public void DamascusCalendar_formats_gregorian_date_arabic()
    {
        var svc = new DamascusCalendarService();
        var date = new DateTime(2026, 7, 31);
        var formatted = svc.FormatGregorianDate(date);
        Assert.NotNull(formatted);
        Assert.NotEmpty(formatted);
    }

    [Fact]
    public void DamascusCalendar_formats_hijri_date()
    {
        var svc = new DamascusCalendarService();
        var date = new DateTime(2026, 7, 31);
        var hijri = svc.FormatHijriDate(date);
        Assert.NotNull(hijri);
        Assert.Matches(@"^\d{4}/\d{2}/\d{2}$", hijri);
    }

    [Fact]
    public void DamascusCalendar_hijri_returns_null_for_out_of_range()
    {
        var svc = new DamascusCalendarService();
        var hijri = svc.FormatHijriDate(DateTime.MinValue);
        Assert.Null(hijri);
    }

    [Fact]
    public void Docs_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "L10N_001_LOCALIZATION.md")));
    }

    [Fact]
    public void Validate_script_exists()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "l10n001-validate.ps1")));
    }

    [Fact]
    public void ServiceDefaults_resx_files_exist()
    {
        var root = ResolveRepoRoot();
        var resDir = Path.Combine(root, "src", "Shared", "HRManagement.ServiceDefaults",
            "Localization", "Resources");
        Assert.True(System.IO.File.Exists(Path.Combine(resDir, "SharedResources.ar.resx")));
        Assert.True(System.IO.File.Exists(Path.Combine(resDir, "SharedResources.en.resx")));
        Assert.True(System.IO.File.Exists(Path.Combine(resDir, "SharedResources.cs")));
    }

    [Fact]
    public void WebShared_resx_files_exist()
    {
        var root = ResolveRepoRoot();
        var resDir = Path.Combine(root, "src", "Shared", "HRManagement.Web.Shared", "Resources");
        Assert.True(System.IO.File.Exists(Path.Combine(resDir, "SharedResources.resx")));
        Assert.True(System.IO.File.Exists(Path.Combine(resDir, "SharedResources.en.resx")));
        Assert.True(System.IO.File.Exists(Path.Combine(resDir, "SharedResources.cs")));
    }

    [Fact]
    public void Resx_files_contain_required_keys()
    {
        var root = ResolveRepoRoot();
        var arPath = Path.Combine(root, "src", "Shared", "HRManagement.ServiceDefaults",
            "Localization", "Resources", "SharedResources.ar.resx");
        var content = System.IO.File.ReadAllText(arPath);

        string[] requiredKeys =
            ["Save", "Cancel", "Search", "Login", "Logout", "Home", "Employees", "Payroll"];

        foreach (var key in requiredKeys)
            Assert.Contains($"name=\"{key}\"", content);
    }

    [Fact]
    public void English_resx_keys_match_arabic()
    {
        var root = ResolveRepoRoot();
        var resDir = Path.Combine(root, "src", "Shared", "HRManagement.ServiceDefaults",
            "Localization", "Resources");

        var arKeys = ExtractResxKeys(Path.Combine(resDir, "SharedResources.ar.resx"));
        var enKeys = ExtractResxKeys(Path.Combine(resDir, "SharedResources.en.resx"));

        Assert.Equal(arKeys.OrderBy(k => k), enKeys.OrderBy(k => k));
    }

    [Fact]
    public void CultureController_exists()
    {
        var root = ResolveRepoRoot();
        var path = Path.Combine(root, "src", "Shared", "HRManagement.ServiceDefaults",
            "Localization", "CultureController.cs");
        Assert.True(System.IO.File.Exists(path));
    }

    [Fact]
    public void Gateway_wires_localization()
    {
        var root = ResolveRepoRoot();
        var path = Path.Combine(root, "src", "Gateway", "HRManagement.Gateway", "Program.cs");
        var text = System.IO.File.ReadAllText(path);
        Assert.Contains("AddHrLocalization", text);
        Assert.Contains("UseHrLocalization", text);
    }

    [Fact]
    public void HRCore_wires_localization()
    {
        var root = ResolveRepoRoot();
        var path = Path.Combine(root, "src", "Services", "HRCore", "HR.Core.Web", "Program.cs");
        var text = System.IO.File.ReadAllText(path);
        Assert.Contains("AddHrLocalization", text);
        Assert.Contains("UseHrLocalization", text);
    }

    private static HashSet<string> ExtractResxKeys(string path)
    {
        var keys = new HashSet<string>();
        foreach (var line in System.IO.File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("<data name=\""))
            {
                var nameStart = trimmed.IndexOf('"') + 1;
                var nameEnd = trimmed.IndexOf('"', nameStart);
                if (nameEnd > nameStart)
                    keys.Add(trimmed[nameStart..nameEnd]);
            }
        }
        return keys;
    }
}
