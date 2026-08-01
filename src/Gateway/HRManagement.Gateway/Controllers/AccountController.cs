using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Gateway.Controllers;

public class AccountController : Controller
{
    [HttpGet]
    [Authorize]
    public IActionResult Login()
    {
        return Redirect("/");
    }

    /// <summary>
    /// Sign out from gateway and Keycloak. GET is used so logout works from proxied /ui/* module pages
    /// (module-rendered antiforgery tokens cannot validate on the gateway host).
    /// </summary>
    [HttpGet]
    [Authorize]
    public IActionResult Logout()
    {
        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
