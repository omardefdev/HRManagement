using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.ServiceDefaults.Localization;

/// <summary>
/// L10N-001: Language toggle endpoint. POST /api/culture?culture=en or culture=ar-SY
/// sets the culture cookie and redirects back.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CultureController : ControllerBase
{
    [HttpPost]
    public IActionResult SetCulture([FromQuery] string culture, [FromQuery] string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return BadRequest("معلمة culture مطلوبة.");

        var supported = HrLocalizationExtensions.SupportedCultures;
        if (!supported.Any(c => c.Name.Equals(culture, StringComparison.OrdinalIgnoreCase)))
            return BadRequest($"الثقافة '{culture}' غير مدعومة. المدعوم: {string.Join(", ", supported.Select(c => c.Name))}");

        Response.Cookies.Append(
            HrLocalizationExtensions.CultureCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return Ok(new { culture, message = "تم تحديث الثقافة." });
    }

    [HttpGet]
    public IActionResult GetCulture()
    {
        var current = HttpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture;
        return Ok(new
        {
            culture = current?.Culture.Name ?? HrLocalizationExtensions.DefaultCultureName,
            uiCulture = current?.UICulture.Name ?? HrLocalizationExtensions.DefaultCultureName,
            supported = HrLocalizationExtensions.SupportedCultures.Select(c => c.Name)
        });
    }
}
