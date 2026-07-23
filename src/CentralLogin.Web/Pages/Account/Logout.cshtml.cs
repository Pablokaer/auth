using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CentralLogin.Web.Pages.Account;

// POST handlers are automatically protected by antiforgery (CSRF) in Razor Pages.
[Authorize]
public sealed class LogoutModel : PageModel
{
    public void OnGet()
    {
    }

    /// <summary>Local logout — clears only this application's cookie.</summary>
    public async Task<IActionResult> OnPostLocalAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Index");
    }

    /// <summary>
    /// Global logout — clears the local cookie and ends the Keycloak SSO
    /// session via the OpenID Connect end-session endpoint, returning to the
    /// configured post-logout redirect URI.
    /// </summary>
    public IActionResult OnPostGlobal()
    {
        var properties = new AuthenticationProperties { RedirectUri = "/" };

        return SignOut(
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
