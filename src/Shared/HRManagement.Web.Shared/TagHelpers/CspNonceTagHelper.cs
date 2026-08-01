using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using HRManagement.ServiceDefaults;

namespace HRManagement.Web.Shared.TagHelpers;

/// <summary>Adds CSP nonce from HttpContext to script/style tags (UX-001).</summary>
[HtmlTargetElement("script")]
[HtmlTargetElement("style")]
public sealed class CspNonceTagHelper : TagHelper
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CspNonceTagHelper(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var nonce = _httpContextAccessor.HttpContext?.GetCspNonce();
        if (string.IsNullOrEmpty(nonce))
            return;

        if (!output.Attributes.ContainsName("nonce"))
            output.Attributes.SetAttribute("nonce", nonce);
    }
}
