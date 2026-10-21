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

    public static IHtmlContent HrStoredEnumDisplay(this IHtmlHelper html, string enumTypeName, string? storedValue)
        => new HtmlString(System.Net.WebUtility.HtmlEncode(EnumDisplayExtensions.GetStoredEnumDisplayName(enumTypeName, storedValue)));

    /// <summary>L10N-003: enum dropdown labels via [Display(Name)] (Arabic).</summary>
    public static IEnumerable<SelectListItem> HrEnumSelectList<TEnum>(this IHtmlHelper html) where TEnum : struct, Enum
        => Enum.GetValues<TEnum>().Select(value => new SelectListItem(
            EnumDisplayExtensions.GetDisplayName(value),
            value.ToString()));
}
