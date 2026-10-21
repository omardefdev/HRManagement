using HRManagement.SharedKernel.Localization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace HRManagement.Web.Shared.TagHelpers;

[HtmlTargetElement("span", Attributes = "hr-enum-display")]
public sealed class HrEnumDisplayTagHelper : TagHelper
{
    [HtmlAttributeName("hr-enum-display")]
    public Enum? Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.Content.SetContent(Value is null ? "—" : EnumDisplayExtensions.GetDisplayName(Value));
    }
}
