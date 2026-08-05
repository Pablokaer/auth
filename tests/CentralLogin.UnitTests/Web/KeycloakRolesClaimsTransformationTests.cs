using System.Security.Claims;
using CentralLogin.Web.Authentication;
using Xunit;

namespace CentralLogin.UnitTests.Web;

public sealed class KeycloakRolesClaimsTransformationTests
{
    private static readonly KeycloakAuthenticationOptions Options = new()
    {
        ClientId = "central-login-web",
    };

    private static ClaimsPrincipal AuthenticatedPrincipal(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private static IEnumerable<Claim> RoleClaims(ClaimsPrincipal principal)
        => principal.FindAll(ClaimTypes.Role);

    [Fact]
    public async Task Realm_roles_are_projected_as_role_claims()
    {
        var transformation = new KeycloakRolesClaimsTransformation(Options);
        var principal = AuthenticatedPrincipal(
            ("realm_access", "{\"roles\":[\"user\",\"administrator\"]}"));

        var result = await transformation.TransformAsync(principal);

        var roles = RoleClaims(result).Select(c => c.Value).ToArray();
        Assert.Contains("user", roles);
        Assert.Contains("administrator", roles);
    }

    [Fact]
    public async Task Client_roles_for_the_configured_client_are_projected_as_role_claims()
    {
        var transformation = new KeycloakRolesClaimsTransformation(Options);
        var principal = AuthenticatedPrincipal(
            ("resource_access", "{\"central-login-web\":{\"roles\":[\"administrator\"]}}"));

        var result = await transformation.TransformAsync(principal);

        var roles = RoleClaims(result).Select(c => c.Value).ToArray();
        Assert.Contains("administrator", roles);
    }

    [Fact]
    public async Task Client_roles_for_a_non_configured_client_are_ignored()
    {
        var transformation = new KeycloakRolesClaimsTransformation(Options);
        var principal = AuthenticatedPrincipal(
            ("resource_access", "{\"some-other-client\":{\"roles\":[\"administrator\"]}}"));

        var result = await transformation.TransformAsync(principal);

        Assert.Empty(RoleClaims(result));
    }

    [Fact]
    public async Task Unauthenticated_principal_is_returned_unchanged()
    {
        var transformation = new KeycloakRolesClaimsTransformation(Options);
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await transformation.TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.Empty(RoleClaims(result));
    }

    [Fact]
    public async Task Malformed_realm_access_does_not_throw_and_adds_no_roles()
    {
        var transformation = new KeycloakRolesClaimsTransformation(Options);
        var principal = AuthenticatedPrincipal(("realm_access", "{not-json"));

        var result = await transformation.TransformAsync(principal);

        Assert.Empty(RoleClaims(result));
    }

    [Fact]
    public async Task Unauthenticated_principal_keeps_its_realm_roles_unprojected()
    {
        // Stronger than the empty-identity case above: the claim IS present and
        // well-formed, so the only thing stopping projection is the
        // !identity.IsAuthenticated guard. Deleting that guard fails this test.
        var transformation = new KeycloakRolesClaimsTransformation(Options);
        var identity = new ClaimsIdentity(
            new[] { new Claim("realm_access", "{\"roles\":[\"administrator\"]}") });
        var principal = new ClaimsPrincipal(identity);

        var result = await transformation.TransformAsync(principal);

        Assert.False(result.Identity!.IsAuthenticated);
        Assert.Empty(RoleClaims(result));
    }

    [Fact]
    public async Task Configured_client_roles_survive_alongside_a_non_configured_client()
    {
        // Positive control for the ClientId filter: proves the non-configured
        // client's roles are dropped *selectively*, not that the whole
        // resource_access branch silently no-ops.
        var transformation = new KeycloakRolesClaimsTransformation(Options);
        var principal = AuthenticatedPrincipal(
            ("resource_access",
                "{\"central-login-web\":{\"roles\":[\"kept\"]},"
                + "\"some-other-client\":{\"roles\":[\"dropped\"]}}"));

        var result = await transformation.TransformAsync(principal);

        var roles = RoleClaims(result).Select(c => c.Value).ToArray();
        Assert.Contains("kept", roles);
        Assert.DoesNotContain("dropped", roles);
    }

    [Fact]
    public async Task Malformed_realm_access_does_not_block_valid_client_roles()
    {
        // Proves the malformed claim is skipped per-claim rather than aborting
        // the whole transformation: the valid client role must still project.
        var transformation = new KeycloakRolesClaimsTransformation(Options);
        var principal = AuthenticatedPrincipal(
            ("realm_access", "{not-json"),
            ("resource_access", "{\"central-login-web\":{\"roles\":[\"administrator\"]}}"));

        var result = await transformation.TransformAsync(principal);

        Assert.Single(RoleClaims(result), c => c.Value == "administrator");
    }

    [Fact]
    public async Task Transforming_twice_does_not_duplicate_role_claims()
    {
        var transformation = new KeycloakRolesClaimsTransformation(Options);
        var principal = AuthenticatedPrincipal(
            ("realm_access", "{\"roles\":[\"administrator\"]}"));

        await transformation.TransformAsync(principal);
        var result = await transformation.TransformAsync(principal);

        Assert.Single(RoleClaims(result), c => c.Value == "administrator");
    }
}
