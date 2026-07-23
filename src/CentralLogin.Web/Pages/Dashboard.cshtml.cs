using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CentralLogin.Web.Pages;

// Authorization is applied by convention in Program.cs (RequireUser policy).
public sealed class DashboardModel : PageModel
{
    public string Subject { get; private set; } = "—";
    public string? Email { get; private set; }
    public string Roles { get; private set; } = "—";

    public void OnGet()
    {
        Subject = User.FindFirst("sub")?.Value
                  ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? "—";
        Email = User.FindFirst("email")?.Value;

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        Roles = roles.Length > 0 ? string.Join(", ", roles) : "—";
    }
}
