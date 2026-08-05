using CentralLogin.IntegrationTests.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CentralLogin.IntegrationTests.Authorization;

/// <summary>
/// Regression guard for the deny-by-default authorization fallback policy
/// (see <c>AddApplicationAuthorization</c>): every discovered Razor Page
/// endpoint must explicitly declare either authorization requirements
/// (<see cref="IAuthorizeData"/>, via <c>[Authorize]</c> or an
/// <c>AuthorizePage</c> convention) or an explicit opt-out via
/// <see cref="IAllowAnonymous"/>. A page with neither is exactly the
/// "add a page and forget to protect it" scenario the fallback policy exists
/// to prevent — this fails the moment such a page is added, without waiting
/// for someone to notice at runtime.
/// </summary>
public sealed class EndpointAuthorizationCoverageTests : IClassFixture<CentralLoginWebFactory>
{
    private readonly CentralLoginWebFactory _factory;

    public EndpointAuthorizationCoverageTests(CentralLoginWebFactory factory) => _factory = factory;

    // The pages the app is expected to expose. This is deliberately a concrete
    // set rather than a plain Assert.NotEmpty: discovery regressing to a
    // single endpoint (or losing one of these) would still pass a
    // "not empty" check, but must fail here.
    private static readonly string[] ExpectedPages =
    [
        "/Index",
        "/Dashboard",
        "/Admin",
        "/Account/Login",
        "/Account/Logout",
        "/Account/AccessDenied",
        "/Error",
    ];

    [Fact]
    public void Every_razor_page_endpoint_declares_its_authorization_intent()
    {
        // Accessing _factory.Services (rather than creating an HTTP client)
        // already starts the test host: WebApplicationFactory.Services calls
        // EnsureServer() internally, which is enough for routing to have
        // discovered all endpoints below.
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();

        var pageEndpoints = dataSource.Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<PageActionDescriptor>() is not null)
            .ToList();

        var discoveredPages = pageEndpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<PageActionDescriptor>()!.ViewEnginePath)
            .ToList();

        foreach (var expectedPage in ExpectedPages)
        {
            Assert.Contains(expectedPage, discoveredPages);
        }

        foreach (var endpoint in pageEndpoints)
        {
            var hasAuthorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            var hasAllowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

            Assert.True(
                hasAuthorizeData || hasAllowAnonymous,
                $"Endpoint '{endpoint.DisplayName}' has no authorization metadata and no " +
                "[AllowAnonymous] opt-out. Every page must explicitly require authorization " +
                "(an [Authorize]/AuthorizePage convention) or opt out with [AllowAnonymous] — " +
                "no page may rely on being implicitly reachable.");
        }
    }
}
