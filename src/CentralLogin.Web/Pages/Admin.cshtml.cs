using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CentralLogin.Web.Pages;

// Authorization is applied by convention in Program.cs (RequireAdministrator policy).
public sealed class AdminModel : PageModel
{
    public void OnGet()
    {
    }
}
