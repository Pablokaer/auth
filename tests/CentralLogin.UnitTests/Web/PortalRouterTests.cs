using System.Security.Claims;
using CentralLogin.UnitTests.Fakes;
using CentralLogin.Web.Authentication;
using Microsoft.Extensions.Logging;
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

        // Normalised (Uri.AbsoluteUri) form of the configured origin-only
        // URL — see PortalUrlPolicy.TryParse — which gains a trailing slash.
        Assert.Equal("http://localhost:5001/", url);
    }

    [Fact]
    public void Administrator_role_routes_to_admin_portal()
    {
        var router = new PortalRouter(Options);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("administrator"));

        Assert.Equal("http://localhost:5100/", url);
    }

    [Fact]
    public void Administrator_wins_over_lower_privilege_roles()
    {
        var router = new PortalRouter(Options);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("user", "employee", "administrator"));

        Assert.Equal("http://localhost:5100/", url);
    }

    [Fact]
    public void Employee_wins_over_user()
    {
        var router = new PortalRouter(Options);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("user", "employee"));

        Assert.Equal("http://localhost:5001/", url);
    }

    [Fact]
    public void Administrator_with_invalid_admin_url_stays_on_central_even_when_also_employee()
    {
        // Pins the deliberate behaviour documented on ResolveHomePortalUrl's
        // administrator branch: a principal with BOTH roles and an
        // unset/invalid AdminUrl stays on central (falls through to the
        // local return URL) rather than falling through further to the
        // (valid) employee portal.
        var router = new PortalRouter(
            new PortalRoutingOptions { AdminUrl = "not a url", EmployeeUrl = "http://localhost:5001" });

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("employee", "administrator"));

        Assert.Null(url);
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

    [Theory]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    public void Invalid_admin_portal_url_falls_through_and_logs_a_warning(string invalidUrl)
    {
        var logger = new CapturingLogger<PortalRouter>();
        var router = new PortalRouter(
            new PortalRoutingOptions { AdminUrl = invalidUrl },
            logger);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("administrator"));

        Assert.Null(url);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Admin"));
    }

    [Fact]
    public void Http_portal_url_is_rejected_when_https_is_required()
    {
        var logger = new CapturingLogger<PortalRouter>();
        var router = new PortalRouter(
            new PortalRoutingOptions { EmployeeUrl = "http://localhost:5001" },
            logger,
            requireHttps: true);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("employee"));

        Assert.Null(url);
        // The message must name the actual reason (https required), not the
        // generic "not an absolute http/https URL" - the URL IS an absolute
        // http URL, it is just the wrong scheme for this environment.
        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning
                && e.Message.Contains("Employee")
                && e.Message.Contains("https is required")
                && !e.Message.Contains("not an absolute"));
    }

    [Fact]
    public void Https_portal_url_is_accepted_when_https_is_required()
    {
        var logger = new CapturingLogger<PortalRouter>();
        var router = new PortalRouter(
            new PortalRoutingOptions { EmployeeUrl = "https://employee.example.com" },
            logger,
            requireHttps: true);

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("employee"));

        Assert.Equal("https://employee.example.com/", url);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(" https://admin.example.com ")]
    [InlineData("https://admin.example.com\n")]
    public void Whitespace_padded_admin_url_is_normalised_with_no_whitespace_or_crlf(string paddedUrl)
    {
        var router = new PortalRouter(new PortalRoutingOptions { AdminUrl = paddedUrl });

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("administrator"));

        Assert.NotNull(url);
        Assert.DoesNotContain(' ', url);
        Assert.DoesNotContain('\r', url);
        Assert.DoesNotContain('\n', url);
    }

    [Fact]
    public void Malformed_realm_access_stays_on_central_and_logs_a_warning()
    {
        var logger = new CapturingLogger<PortalRouter>();
        // "invalid JSON literal" shape (see the matching test in
        // KeycloakRolesClaimsTransformationTests for why this — rather than a
        // structurally broken payload like "{not-json" — is required for the
        // sentinel assertion below to be load-bearing).
        var identity = new ClaimsIdentity(
            new[] { new Claim("realm_access", "{\"roles\":tSECRETVALUE}") },
            authenticationType: "Test");
        var principal = new ClaimsPrincipal(identity);
        var router = new PortalRouter(Options, logger);

        // Fail-open: a malformed claim still routes as "no realm roles"
        // rather than failing sign-in.
        var url = router.ResolveHomePortalUrl(principal);

        Assert.Null(url);

        Assert.Contains(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("realm_access"));

        // AC-2: neither the rendered message nor the (deliberately not
        // passed) exception may carry the raw claim content. See the
        // matching assertion in KeycloakRolesClaimsTransformationTests for
        // why CapturingLogger records the exception separately.
        Assert.DoesNotContain(
            logger.Entries,
            e => e.Message.Contains("SECRETVALUE")
                || (e.Exception?.Message.Contains("SECRETVALUE") ?? false));
    }

    [Fact]
    public void Backslash_separator_admin_url_does_not_survive_as_the_raw_string()
    {
        // "http:/\evil.com" is a classic backslash-as-separator trick:
        // Uri.TryCreate happily parses it (host becomes evil.com), but the
        // raw string must never reach ReturnUri verbatim - PortalRouter must
        // return the parsed/normalised Uri.AbsoluteUri instead.
        const string trick = "http:/" + "\\" + "evil.com";
        var router = new PortalRouter(new PortalRoutingOptions { AdminUrl = trick });

        var url = router.ResolveHomePortalUrl(PrincipalWithRealmRoles("administrator"));

        Assert.NotEqual(trick, url);
        Assert.Equal("http://evil.com/", url);
    }
}
