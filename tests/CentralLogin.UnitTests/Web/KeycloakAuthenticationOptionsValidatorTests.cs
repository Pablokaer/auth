using CentralLogin.UnitTests.Fakes;
using CentralLogin.Web.Authentication;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CentralLogin.UnitTests.Web;

public sealed class KeycloakAuthenticationOptionsValidatorTests
{
    private static KeycloakAuthenticationOptionsValidator CreateValidator(string environmentName)
        => new(new FakeHostEnvironment(environmentName));

    /// <summary>A fully configured section, as a deployment is meant to supply it.</summary>
    private static KeycloakAuthenticationOptions ValidOptions() => new()
    {
        Authority = "https://auth.example.com/realms/central-login",
        ClientId = "central-login-web",
        ClientSecret = "a-real-secret",
        RequireHttpsMetadata = true,
    };

    [Fact]
    public void Fully_configured_options_pass_outside_development()
    {
        var result = CreateValidator(Environments.Production).Validate(name: null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Defaults_fail_because_a_missing_section_binds_to_them()
    {
        // Get<T>() of an absent section used to yield exactly this object, which
        // the old `?? new()` fallback accepted; ClientId has a usable default, so
        // the failures name the two values a deployment must actually supply.
        var result = CreateValidator(Environments.Production).Validate(name: null, new KeycloakAuthenticationOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains($"Keycloak:{nameof(KeycloakAuthenticationOptions.Authority)}"));
        Assert.Contains(result.Failures!, f => f.Contains($"Keycloak:{nameof(KeycloakAuthenticationOptions.ClientSecret)}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_client_secret_fails_and_says_where_to_put_it(string clientSecret)
    {
        var options = ValidOptions();
        options.ClientSecret = clientSecret;

        var result = CreateValidator(Environments.Production).Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains($"Keycloak:{nameof(KeycloakAuthenticationOptions.ClientSecret)}")
                                              && f.Contains("user-secrets"));
    }

    [Fact]
    public void Empty_client_id_fails()
    {
        var options = ValidOptions();
        options.ClientId = "";

        var result = CreateValidator(Environments.Production).Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains($"Keycloak:{nameof(KeycloakAuthenticationOptions.ClientId)}"));
    }

    [Fact]
    public void Placeholder_authority_fails_even_though_it_is_a_valid_https_url()
    {
        var options = ValidOptions();
        options.Authority = "https://REPLACE_WITH_KEYCLOAK_HOST/realms/central-login";

        var result = CreateValidator(Environments.Production).Validate(name: null, options);

        Assert.True(result.Failed);

        // One failure only: the placeholder reason, not also a URL-shape
        // complaint about a value that happens to parse fine.
        var authorityFailures = result.Failures!
            .Where(f => f.Contains($"Keycloak:{nameof(KeycloakAuthenticationOptions.Authority)}"))
            .ToList();
        Assert.Single(authorityFailures);
        Assert.Contains("placeholder", authorityFailures[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Placeholder_client_secret_fails_without_echoing_the_value()
    {
        const string secret = "REPLACE_WITH_CLIENT_SECRET";
        var options = ValidOptions();
        options.ClientSecret = secret;

        var result = CreateValidator(Environments.Development).Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains($"Keycloak:{nameof(KeycloakAuthenticationOptions.ClientSecret)}")
                                              && f.Contains("placeholder", StringComparison.OrdinalIgnoreCase));

        // The marker is only a heuristic, so this can fire on a real secret -
        // whose value must never reach the startup log. Contrast with Authority,
        // which is not sensitive and is quoted to help the operator.
        Assert.DoesNotContain(result.Failures!, f => f.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void Non_absolute_authority_fails()
    {
        var options = ValidOptions();
        options.Authority = "auth.example.com/realms/central-login";

        var result = CreateValidator(Environments.Development).Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("must be an absolute") && !f.Contains("https is required"));
    }

    [Fact]
    public void Http_authority_fails_outside_development_naming_https_as_the_reason()
    {
        var options = ValidOptions();
        options.Authority = "http://auth.example.com/realms/central-login";

        var result = CreateValidator(Environments.Production).Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("https is required") && !f.Contains("must be an absolute"));
    }

    [Fact]
    public void Http_authority_with_metadata_check_disabled_passes_in_development()
    {
        // Exactly what appsettings.Development.json + the AppHost supply.
        var options = ValidOptions();
        options.Authority = "http://localhost:8080/realms/central-login";
        options.RequireHttpsMetadata = false;

        var result = CreateValidator(Environments.Development).Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Disabling_https_metadata_fails_outside_development()
    {
        var options = ValidOptions();
        options.RequireHttpsMetadata = false;

        var result = CreateValidator(Environments.Production).Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains($"Keycloak:{nameof(KeycloakAuthenticationOptions.RequireHttpsMetadata)}"));
    }
}
