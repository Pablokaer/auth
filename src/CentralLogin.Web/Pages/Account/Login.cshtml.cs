using CentralLogin.Web.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CentralLogin.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting(RateLimitingPolicies.Authentication)]
public sealed class LoginModel : PageModel
{
    /// <summary>Initiates the OpenID Connect challenge (Authorization Code + PKCE).</summary>
    public IActionResult OnGet(string? returnUrl = null)
    {
        var redirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";

        return Challenge(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = redirectUri },
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
