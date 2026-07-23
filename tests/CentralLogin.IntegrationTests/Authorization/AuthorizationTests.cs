using System.Net;
using CentralLogin.Application.Authorization;
using CentralLogin.IntegrationTests.Common;
using Xunit;

namespace CentralLogin.IntegrationTests.Authorization;

/// <summary>
/// Authorization behaviour for authenticated users, unauthenticated users,
/// administrators, and users lacking the required role.
/// </summary>
public sealed class AuthorizationTests : IClassFixture<CentralLoginWebFactory>
{
    private readonly CentralLoginWebFactory _factory;

    public AuthorizationTests(CentralLoginWebFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    [Fact]
    public async Task Home_is_public()
    {
        var response = await CreateClient().GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_is_denied_when_unauthenticated()
    {
        var response = await CreateClient().GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_is_allowed_for_a_user()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "grace");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, ApplicationRoles.User);

        var response = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_is_forbidden_for_a_user_without_the_administrator_role()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "grace");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, ApplicationRoles.User);

        var response = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_is_allowed_for_an_administrator()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "root");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, $"{ApplicationRoles.User},{ApplicationRoles.Administrator}");

        var response = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
