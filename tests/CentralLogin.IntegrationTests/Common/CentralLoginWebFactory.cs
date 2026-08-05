using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

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

            // LoginModel challenges the OpenIdConnect scheme explicitly (not the
            // default scheme above), so it still runs the real handler. Without
            // this, the handler fetches the discovery document from the
            // unreachable test-only Authority and the request 500s. The built-in
            // OpenIdConnectPostConfigureOptions (registered by AddOpenIdConnect,
            // and run before test PostConfigure callbacks) already builds
            // Options.ConfigurationManager from Options.Configuration/
            // MetadataAddress by the time this runs, so setting
            // Options.Configuration alone is too late — the handler reads
            // ConfigurationManager, not Configuration, at request time.
            // Replacing ConfigurationManager directly with a static one skips
            // the network call entirely, so the resulting 302 to the
            // authorization endpoint can be asserted directly.
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, oidc =>
            {
                oidc.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = "https://localhost/realms/central-login",
                    AuthorizationEndpoint = "https://localhost/realms/central-login/protocol/openid-connect/auth",
                    TokenEndpoint = "https://localhost/realms/central-login/protocol/openid-connect/token",
                    JwksUri = "https://localhost/realms/central-login/protocol/openid-connect/certs",
                };
                oidc.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc.Configuration);
            });

            // Endpoint with no authorization metadata at all, used only by
            // FallbackPolicyTests to prove the deny-by-default fallback policy
            // actually rejects unattributed endpoints.
            services.AddSingleton<IStartupFilter, TestOnlyUnprotectedEndpointStartupFilter>();
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
