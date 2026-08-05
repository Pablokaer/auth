using System.Net;
using CentralLogin.IntegrationTests.Common;
using Xunit;

namespace CentralLogin.IntegrationTests.Routing;

/// <summary>
/// The deny-by-default fallback authorization policy (see
/// <c>AddApplicationAuthorization</c>) is also evaluated for requests that
/// match no endpoint at all, not just endpoints missing authorization
/// metadata — so without the anonymous catch-all fallback route in
/// <c>Program.cs</c>, an unmatched path would challenge anonymous users
/// (an OIDC redirect) instead of plainly 404-ing. These tests guard that an
/// unmatched request still gets a plain, anonymous 404, and that real static
/// files and the home page are unaffected.
/// </summary>
public sealed class FallbackRoutingTests : IClassFixture<CentralLoginWebFactory>
{
    private readonly CentralLoginWebFactory _factory;

    public FallbackRoutingTests(CentralLoginWebFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    [Fact]
    public async Task An_unknown_extensionless_path_404s_for_an_anonymous_caller()
    {
        var response = await CreateClient().GetAsync("/nonexistent-xyz");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_path_with_a_file_extension_404s_for_an_anonymous_caller()
    {
        // The parameterless MapFallback() overload uses the "/{*path:nonfile}"
        // pattern, which does NOT match paths with a file extension — exactly
        // the case (favicon.ico, robots.txt, ...) that must not fall through
        // to an OIDC challenge. This asserts the explicit "/{**path}" pattern
        // is used instead.
        var response = await CreateClient().GetAsync("/favicon.ico");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_file_under_an_existing_static_directory_404s_for_an_anonymous_caller()
    {
        var response = await CreateClient().GetAsync("/css/does-not-exist.css");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_real_static_file_is_still_served()
    {
        var response = await CreateClient().GetAsync("/css/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Root_still_resolves_to_the_index_page_rather_than_the_fallback()
    {
        var response = await CreateClient().GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.ToString());
    }
}
