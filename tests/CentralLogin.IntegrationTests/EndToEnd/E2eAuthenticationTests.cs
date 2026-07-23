#if E2E
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace CentralLogin.IntegrationTests.EndToEnd;

/// <summary>
/// Optional end-to-end profile. Runs the full Aspire environment (Web +
/// Keycloak + PostgreSQL) and checks the app is reachable. Enable with:
///
///   dotnet test tests/CentralLogin.IntegrationTests -p:E2E=true
///
/// Requires a container runtime (Docker) and is kept OUT of the default CI run.
/// </summary>
[Trait("Category", "E2E")]
public sealed class E2eAuthenticationTests
{
    [Fact]
    public async Task Web_starts_under_aspire_and_home_is_reachable()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.CentralLogin_AppHost>();

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        await app.ResourceNotifications.WaitForResourceHealthyAsync("web");

        var client = app.CreateHttpClient("web");
        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
    }
}
#endif
