using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CentralLogin.IntegrationTests.Common;

/// <summary>
/// Boots the real Web application for integration tests, but swaps Keycloak/OIDC
/// authentication for the <see cref="TestAuthHandler"/> and points SQLite at a
/// unique temp directory.
/// </summary>
public sealed class CentralLoginWebFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "centrallogin-it", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DataDirectory"] = _dataDirectory,
                ["Database:FileName"] = "it.db",
                ["Keycloak:Authority"] = "https://localhost/realms/central-login",
                ["Keycloak:RequireHttpsMetadata"] = "false",
                ["Keycloak:ClientSecret"] = "test-only-not-a-real-secret",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_dataDirectory))
                {
                    Directory.Delete(_dataDirectory, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
