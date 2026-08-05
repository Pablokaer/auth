using CentralLogin.IntegrationTests.Common;
using CentralLogin.Web.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace CentralLogin.IntegrationTests.Configuration;

/// <summary>
/// Guards <c>.ValidateOnStart()</c> + the <see cref="IValidateOptions{TOptions}"/>
/// registration for <see cref="KeycloakAuthenticationOptions"/> in
/// <c>AuthenticationSetup.AddKeycloakAuthentication</c>: the app must refuse to
/// start on an unconfigured Keycloak section rather than fail at a user's first
/// login. Nothing else in this suite would notice if the validation were removed,
/// because <see cref="CentralLoginWebFactory"/> replaces the OIDC handler with
/// <see cref="Common.TestAuthHandler"/> and so never exercises these values.
/// </summary>
public sealed class KeycloakOptionsStartupValidationTests
{
    /// <summary>
    /// Setting a key to <c>null</c> in the last-added configuration source is how
    /// this suite models "not configured": it overrides both the value shipped in
    /// appsettings.json and the one <see cref="CentralLoginWebFactory"/> injects.
    /// Note this is not identical to an absent section — a present-but-null key
    /// binds to the empty string, whereas an absent one leaves the property's
    /// initialiser in place — so it is only asserted on for the two values that
    /// have no usable default and must come from configuration.
    /// </summary>
    private static WebApplicationFactory<Program> FactoryWith(params (string Key, string? Value)[] overrides)
        => new CentralLoginWebFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    overrides.ToDictionary(o => o.Key, o => o.Value))));

    [Fact]
    public void Unconfigured_keycloak_section_fails_startup_naming_the_required_keys()
    {
        using var factory = FactoryWith(
            ("Keycloak:Authority", null),
            ("Keycloak:ClientSecret", null));

        var failures = AssertStartupRejected(factory);

        // The two keys a deployment must supply; ClientId has a working default,
        // and is covered by KeycloakAuthenticationOptionsValidatorTests.
        Assert.Contains(failures, f => f.Contains("Keycloak:Authority", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Contains("Keycloak:ClientSecret", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_client_secret_fails_startup()
    {
        using var factory = FactoryWith(("Keycloak:ClientSecret", ""));

        var failures = AssertStartupRejected(factory);

        Assert.Contains(failures, f => f.Contains("Keycloak:ClientSecret", StringComparison.Ordinal));
    }

    [Fact]
    public void Placeholder_authority_from_appsettings_fails_startup()
    {
        // The literal value shipped in appsettings.json: a syntactically valid
        // https URL, which every other check would accept.
        using var factory = FactoryWith(
            ("Keycloak:Authority", "https://REPLACE_WITH_KEYCLOAK_HOST/realms/central-login"));

        var failures = AssertStartupRejected(factory);

        Assert.Contains(failures, f => f.Contains("Keycloak:Authority", StringComparison.Ordinal)
                                       && f.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Valid_configuration_starts_and_serves_requests()
    {
        // The factory's own configuration is complete and valid, so this is the
        // control case: validation must not reject a correctly configured app.
        using var factory = new CentralLoginWebFactory();

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/");

        Assert.True(response.IsSuccessStatusCode);

        // The validated options are what DI hands out - no unvalidated instance.
        var options = factory.Services.GetRequiredService<KeycloakAuthenticationOptions>();
        Assert.Equal("https://localhost/realms/central-login", options.Authority);
    }

    /// <summary>
    /// The OIDC handler is configured from <see cref="KeycloakAuthenticationOptions"/>
    /// through the options pipeline rather than from a value captured during
    /// registration. This suite otherwise never exercises that wiring — the
    /// factory replaces the handler — so without this test the Keycloak section
    /// could stop reaching <see cref="OpenIdConnectOptions"/> entirely and every
    /// check above would still pass.
    /// </summary>
    [Fact]
    public void Validated_options_are_what_configure_the_openid_connect_handler()
    {
        using var factory = new CentralLoginWebFactory();

        var keycloak = factory.Services.GetRequiredService<KeycloakAuthenticationOptions>();
        var oidc = factory.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal(keycloak.Authority, oidc.Authority);
        Assert.Equal(keycloak.ClientId, oidc.ClientId);
        Assert.Equal(keycloak.ClientSecret, oidc.ClientSecret);
        Assert.Equal(keycloak.RequireHttpsMetadata, oidc.RequireHttpsMetadata);
        Assert.Equal(keycloak.CallbackPath, oidc.CallbackPath);
        Assert.Equal(keycloak.SignedOutCallbackPath, oidc.SignedOutCallbackPath);

        // The rest of the handler setup must survive the move too.
        Assert.True(oidc.UsePkce);
        Assert.Equal(OpenIdConnectResponseType.Code, oidc.ResponseType);
        Assert.False(oidc.MapInboundClaims);
        Assert.Contains("roles", oidc.Scope);
    }

    private static IEnumerable<string> AssertStartupRejected(WebApplicationFactory<Program> factory)
    {
        var thrown = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validationException = StartupValidationFailure.Unwrap(thrown);
        Assert.NotNull(validationException);
        return validationException!.Failures;
    }
}
