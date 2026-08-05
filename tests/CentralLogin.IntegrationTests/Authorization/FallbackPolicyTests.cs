using System.Net;
using CentralLogin.IntegrationTests.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CentralLogin.IntegrationTests.Authorization;

/// <summary>
/// Regression guard for the deny-by-default fallback authorization policy
/// (<c>AddApplicationAuthorization</c>). Unlike <see cref="AuthorizationTests"/>
/// and <see cref="EndpointAuthorizationCoverageTests"/>, both of which are
/// green even on a base commit that never configured a fallback policy at
/// all (every production page already carries its own authorization
/// metadata), these tests fail if <c>SetFallbackPolicy(...)</c> is removed.
/// </summary>
public sealed class FallbackPolicyTests : IClassFixture<CentralLoginWebFactory>
{
    private readonly CentralLoginWebFactory _factory;

    public FallbackPolicyTests(CentralLoginWebFactory factory) => _factory = factory;

    [Fact]
    public async Task A_fallback_policy_requiring_an_authenticated_user_is_configured()
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback!.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task An_endpoint_with_no_authorization_metadata_is_rejected_for_an_anonymous_caller()
    {
        var client = _factory.CreateClient();

        // TestOnlyUnprotectedEndpointStartupFilter maps this endpoint with no
        // [Authorize], no AuthorizePage convention, and no [AllowAnonymous] —
        // it exists purely to prove the fallback policy is enforced. This is
        // deliberately NOT "request an unmapped path": after the fallback-404
        // fix in Program.cs, an unmapped path 404s for everyone and would
        // pass this assertion even with no fallback policy configured at all.
        var response = await client.GetAsync(TestOnlyUnprotectedEndpointStartupFilter.Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
