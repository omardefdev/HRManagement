using HRManagement.SharedKernel.Localization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace HRManagement.Web.Shared.TagHelpers;

[HtmlTargetElement("span", Attributes = "hr-stored-enum")]
public sealed class HrStoredEnumTagHelper : TagHelper
{
    [HtmlAttributeName("hr-stored-enum")]
    public string EnumType { get; set; } = string.Empty;

    [HtmlAttributeName("value")]
    public string? Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.Content.SetContent(EnumDisplayExtensions.GetStoredEnumDisplayName(EnumType, Value));
    }
}
