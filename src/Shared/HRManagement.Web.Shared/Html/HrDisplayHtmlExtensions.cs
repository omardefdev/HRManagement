using HRManagement.SharedKernel.Localization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRManagement.Web.Shared.Html;

public static class HrDisplayHtmlExtensions
{
    public static IHtmlContent HrEnumDisplay<TEnum>(this IHtmlHelper html, TEnum value) where TEnum : struct, Enum
        => new HtmlString(System.Net.WebUtility.HtmlEncode(EnumDisplayExtensions.GetDisplayName(value)));

    public static IHtmlContent HrEnumDisplay(this IHtmlHelper html, Enum value)
        => new HtmlString(System.Net.WebUtility.HtmlEncode(EnumDisplayExtensions.GetDisplayName(value)));

    /// <summary>PLAN-67 (G-UX-03): a nullable enum, by its Arabic name; <paramref name="missing"/> when it has no value.</summary>
    public static IHtmlContent HrEnumDisplay<TEnum>(this IHtmlHelper html, TEnum? value, string missing = "—") where TEnum : struct, Enum
        => new HtmlString(System.Net.WebUtility.HtmlEncode(value is { } v ? EnumDisplayExtensions.GetDisplayName(v) : missing));

    /// <summary>PLAN-67 (G-UX-03): a record type stored as text (an approval's entity type, a policy's subject), by its Arabic name.</summary>
    public static IHtmlContent HrEntityTypeDisplay(this IHtmlHelper html, string? entityType)
        => new HtmlString(System.Net.WebUtility.HtmlEncode(EntityTypeNamesAr.Get(entityType)));

    public static IHtmlContent HrStoredEnumDisplay(this IHtmlHelper html, string enumTypeName, string? storedValue)
        => new HtmlString(System.Net.WebUtility.HtmlEncode(EnumDisplayExtensions.GetStoredEnumDisplayName(enumTypeName, storedValue)));

    /// <summary>
    /// PLAN-67 (P-5, D-67-7): text that may carry internal codes (a change reason, an evidence reference, a reason the catalog gives).
    /// An effect, a service or an action named by its key is replaced by its Arabic name; any other code (<c>POL-049-SCOPE-RETIRE</c>,
    /// <c>D-POL-1</c>, <c>PLAN-46</c>, <c>HIRE-WIZARD</c>) is kept, as secondary text, never inside the user's sentence. The text is encoded.
    /// </summary>
    public static IHtmlContent HrTextWithCodes(this IHtmlHelper html, string? text, string missing = "—")
    {
        if (string.IsNullOrWhiteSpace(text))
            return new HtmlString(System.Net.WebUtility.HtmlEncode(missing));
        var sb = new System.Text.StringBuilder();
        var last = 0;
        foreach (System.Text.RegularExpressions.Match m in InternalCodePattern.Matches(text))
        {
            sb.Append(System.Net.WebUtility.HtmlEncode(text[last..m.Index]));
            sb.Append(TranslateCode(m.Value));
            last = m.Index + m.Length;
        }

        sb.Append(System.Net.WebUtility.HtmlEncode(text[last..]));
        return new HtmlString(sb.ToString());
    }

    private static readonly System.Text.RegularExpressions.Regex InternalCodePattern = new(
        @"\b(?:[A-Z][A-Za-z0-9]*(?:[-.][A-Z0-9][A-Za-z0-9]*)+|[A-Z][a-z]+(?:[A-Z][a-z0-9]+)+|[A-Z]{2,}[0-9]*)\b",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string TranslateCode(string token)
    {
        if (Enum.TryParse<HRManagement.SharedKernel.Policy.PolicyEffect>(token, ignoreCase: false, out var effect)
            && HRManagement.SharedKernel.Policy.PolicyVocabularyAr.Effects.TryGetValue(effect, out var verb)
            && Enum.IsDefined(effect) && !char.IsDigit(token[0]))
            return System.Net.WebUtility.HtmlEncode(verb);
        if (HRManagement.SharedKernel.Policy.PolicyVocabularyAr.Services.TryGetValue(token, out var service))
            return System.Net.WebUtility.HtmlEncode(service);
        if (HRManagement.SharedKernel.Policy.PolicyDomainCatalog.Find(token)?.NameAr is { } domain)
            return System.Net.WebUtility.HtmlEncode(domain);
        if (HRManagement.SharedKernel.Policy.PolicyVocabularyAr.WorkflowCodes.TryGetValue(token, out var workflow))
            return System.Net.WebUtility.HtmlEncode(workflow) + $" <span class=\"hr-internal-code\">{System.Net.WebUtility.HtmlEncode(token)}</span>";
        return $"<span class=\"hr-internal-code\">{System.Net.WebUtility.HtmlEncode(token)}</span>";
    }

    /// <summary>L10N-003: enum dropdown labels via [Display(Name)] (Arabic).</summary>
    public static IEnumerable<SelectListItem> HrEnumSelectList<TEnum>(this IHtmlHelper html) where TEnum : struct, Enum
        => Enum.GetValues<TEnum>().Select(value => new SelectListItem(
            EnumDisplayExtensions.GetDisplayName(value),
            value.ToString()));
}
