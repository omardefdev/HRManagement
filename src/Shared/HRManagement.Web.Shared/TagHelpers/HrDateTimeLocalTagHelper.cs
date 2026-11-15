using System.Globalization;
using HRManagement.ServiceDefaults.Localization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.Web.Shared.TagHelpers;

/// <summary>
/// PLAN-67 (D-67-2, P-7): a <c>datetime-local</c> input bound with <c>asp-for</c> shows the stored UTC value on the user's clock
/// (<c>Localization:TimeZone</c>). It runs after the framework's input tag helper and rewrites only <c>value</c>; a value the user
/// typed and the form sends back (a validation error) is shown as typed. The model binder (<see cref="HRManagement.ServiceDefaults.Localization.HrLocalDateTimeModelBinder"/>)
/// reads the posted value back as local time and stores UTC.
/// </summary>
[HtmlTargetElement("input", Attributes = "asp-for,[type=datetime-local]", TagStructure = TagStructure.WithoutEndTag)]
public sealed class HrDateTimeLocalTagHelper : TagHelper
{
    /// <summary>After <c>InputTagHelper</c> (order -1000), which writes the value first.</summary>
    public override int Order => 1000;

    [HtmlAttributeName("asp-for")]
    public ModelExpression For { get; set; } = default!;

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        if (ViewContext.ViewData.ModelState.TryGetValue(name, out var entry) && entry.AttemptedValue is not null)
            return; // the user's own text, already local

        var zone = ViewContext.HttpContext.RequestServices.GetService<ICalendarService>()?.TimeZone ?? HrLocalTime.DefaultZone;
        var local = For.Model switch
        {
            DateTime d => HrLocalTime.ToLocal(d, zone),
            DateTimeOffset d => HrLocalTime.ToLocal(d, zone),
            _ => (DateTime?)null
        };
        if (local is { } value)
            output.Attributes.SetAttribute("value", value.ToString(HrLocalTime.DateTimeLocalInputFormat, CultureInfo.InvariantCulture));
    }
}
