using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CentralLogin.Web.Pages;

[AllowAnonymous]
public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
