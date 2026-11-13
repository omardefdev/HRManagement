using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRManagement.Web.Shared.Html;

/// <summary>
/// PLAN-67 (§3, D-67-3, G-UX-04): people and records by name, never by id.
/// <list type="bullet">
/// <item><see cref="HrUserName"/>: a user id (Keycloak <c>sub</c>) becomes a <c>[data-hr-user-id]</c> span that <c>display-names.js</c> fills
/// from the gateway's registry; «النظام» for an automatic actor; «مستخدم غير معروف» with the id in a tooltip until (or unless) the registry
/// knows the user. A value that is already a name is printed as it is.</item>
/// <item><see cref="HrEmployeeName"/> and <see cref="HrOrgUnitName"/>: <c>[data-hr-employee-id]</c> and <c>[data-hr-org-unit-id]</c>, filled
/// from HR.Core's lookups.</item>
/// <item><see cref="HrLegalName"/>: a regime or legislation code by its Arabic title, the code as secondary text.</item>
/// </list>
/// </summary>
public static partial class HrNameHtmlExtensions
{
    public const string SystemActorAr = "النظام";
    public const string UnknownUserAr = "مستخدم غير معروف";
    public const string LoadingAr = "جاري التحميل...";

    public static IHtmlContent HrUserName(this IHtmlHelper html, object? userId, string missing = "—")
    {
        var text = userId switch
        {
            null => null,
            Guid g => g == Guid.Empty ? "system" : g.ToString("D"),
            _ => userId.ToString()?.Trim()
        };
        if (string.IsNullOrEmpty(text))
            return new HtmlString(WebUtility.HtmlEncode(missing));
        if (string.Equals(text, "system", StringComparison.OrdinalIgnoreCase))
            return new HtmlString($"<span class=\"hr-user-system\">{SystemActorAr}</span>");
        if (!Guid.TryParse(text, out _))
            return new HtmlString(WebUtility.HtmlEncode(text)); // already a name
        var id = WebUtility.HtmlEncode(text);
        return new HtmlString($"<span class=\"hr-user-name\" data-hr-user-id=\"{id}\" title=\"{id}\">{UnknownUserAr}</span>");
    }

    public static IHtmlContent HrEmployeeName(this IHtmlHelper html, Guid? employeeId, string missing = "—") =>
        employeeId is { } id && id != Guid.Empty
            ? new HtmlString($"<span data-hr-employee-id=\"{id:D}\">{LoadingAr}</span>")
            : new HtmlString(WebUtility.HtmlEncode(missing));

    public static IHtmlContent HrOrgUnitName(this IHtmlHelper html, Guid? orgUnitId, string missing = "—") =>
        orgUnitId is { } id && id != Guid.Empty
            ? new HtmlString($"<span data-hr-org-unit-id=\"{id:D}\">{LoadingAr}</span>")
            : new HtmlString(WebUtility.HtmlEncode(missing));

    /// <summary>A service regime (<c>law50</c>, <c>special_judiciary</c>) or a legislation id (<c>SYR-LAW-50-2004</c>) by its Arabic title.</summary>
    public static IHtmlContent HrLegalName(this IHtmlHelper html, string? code, string missing = "—")
    {
        if (string.IsNullOrWhiteSpace(code))
            return new HtmlString(WebUtility.HtmlEncode(missing));
        var title = LegalTitleAr(code.Trim());
        var secondary = $"<span class=\"hr-internal-code\">{WebUtility.HtmlEncode(code.Trim())}</span>";
        return new HtmlString(title is null ? secondary : WebUtility.HtmlEncode(title) + " " + secondary);
    }

    private static readonly Dictionary<string, string> SpecialRegimes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["judiciary"] = "القضاء",
        ["banking"] = "المصارف",
        ["military"] = "العسكريين",
        ["diplomatic"] = "السلك الدبلوماسي",
        ["teaching"] = "التعليم",
        ["health"] = "القطاع الصحي",
    };

    public static string? LegalTitleAr(string code)
    {
        var law = LawRegimeRegex().Match(code);
        if (law.Success)
            return law.Groups["year"].Success
                ? $"القانون رقم {law.Groups["n"].Value} لعام {law.Groups["year"].Value}"
                : $"نظام القانون رقم {law.Groups["n"].Value}";
        var special = SpecialRegimeRegex().Match(code);
        if (special.Success)
            return SpecialRegimes.TryGetValue(special.Groups["name"].Value, out var ar) ? $"نظام خاص — {ar}" : "نظام خاص";
        var source = LegislationRegex().Match(code);
        if (source.Success)
        {
            var kind = source.Groups["kind"].Value.ToUpperInvariant() switch
            {
                "LAW" => "القانون",
                "DEC" or "DECREE" or "LD" => "المرسوم",
                "RES" or "RESOLUTION" => "القرار",
                "INST" or "INSTRUCTION" => "التعليمات",
                _ => null
            };
            if (kind is not null)
                return $"{kind} رقم {source.Groups["n"].Value} لعام {source.Groups["year"].Value}";
        }

        return null;
    }

    [GeneratedRegex(@"^law(?<n>\d+)(?:_(?<year>\d{4}))?$", RegexOptions.IgnoreCase)]
    private static partial Regex LawRegimeRegex();

    [GeneratedRegex(@"^special_(?<name>[a-z_]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SpecialRegimeRegex();

    [GeneratedRegex(@"^SYR-(?<kind>[A-Z]+)-(?<n>\d+)-(?<year>\d{4})$", RegexOptions.IgnoreCase)]
    private static partial Regex LegislationRegex();
}
