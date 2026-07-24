using System.Security.Claims;
using CentralLogin.Web.Authentication;
using Xunit;

namespace CentralLogin.UnitTests.Web;

public sealed class PortalRouterTests
{
    private static readonly PortalRoutingOptions Options = new()
    {
        AdminUrl = "http://localhost:5100",
        EmployeeUrl = "http://localhost:5001",
    };

    private static ClaimsPrincipal PrincipalWithRealmRoles(params string[] roles)
    {
        var quoted = string.Join(",", roles.Select(r => $"\"{r}\""));
        var realmAccess = $"{{\"roles\":[{quoted}]}}";
        var identity = new ClaimsIdentity(
            new[] { new Claim("realm_access", realmAccess) },
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void User_role_stays_on_central()
    {
        var router = new PortalRouter(Options);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("user"));

        Assert.Null(url);
    }

    [Fact]
    public void Employee_role_routes_to_employee_portal()
    {
        var router = new PortalRouter(Options);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("employee"));

        Assert.Equal("http://localhost:5001", url);
    }

    [Fact]
    public void Administrator_role_routes_to_admin_portal()
    {
        var router = new PortalRouter(Options);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("administrator"));

        Assert.Equal("http://localhost:5100", url);
    }

    [Fact]
    public void Administrator_wins_over_lower_privilege_roles()
    {
        var router = new PortalRouter(Options);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("user", "employee", "administrator"));

        Assert.Equal("http://localhost:5100", url);
    }

    [Fact]
    public void Employee_wins_over_user()
    {
        var router = new PortalRouter(Options);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("user", "employee"));

        Assert.Equal("http://localhost:5001", url);
    }

    [Fact]
    public void No_realm_roles_stays_on_central()
    {
        var router = new PortalRouter(Options);

        var url = router.ResolveHomePortalUrl(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Null(url);
    }

    [Fact]
    public void Missing_portal_url_falls_through_to_central()
    {
        var router = new PortalRouter(new PortalRoutingOptions { AdminUrl = null, EmployeeUrl = null });

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("employee"));

        Assert.Null(url);
    }
}
