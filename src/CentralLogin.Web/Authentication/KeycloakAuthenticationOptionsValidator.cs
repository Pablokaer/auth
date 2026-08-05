using Microsoft.Extensions.Options;

namespace CentralLogin.Web.Authentication;

/// <summary>
/// Fails fast at startup (via <c>ValidateOnStart</c>) when the Keycloak
/// authentication configuration is incomplete or still carries the placeholder
/// values shipped in <c>appsettings.json</c>.
///
/// Without this, a deployment that forgets to override <c>Keycloak:Authority</c>
/// or supply <c>Keycloak:ClientSecret</c> starts and looks healthy to a liveness
/// probe, and the mistake only surfaces as an opaque OIDC metadata /
/// <c>invalid_client</c> failure the first time a real user is redirected — far
/// from its cause. Every value here is mandatory for the sign-in flow, so unlike
/// <see cref="PortalRoutingOptionsValidator"/> an unset value is a failure, not
/// an opt-out.
/// </summary>
public sealed class KeycloakAuthenticationOptionsValidator : IValidateOptions<KeycloakAuthenticationOptions>
{
    /// <summary>
    /// Marker of the placeholder values in <c>appsettings.json</c> (e.g.
    /// <c>https://REPLACE_WITH_KEYCLOAK_HOST/realms/central-login</c>). Those
    /// stay in the file as documentation, so an unconfigured deployment binds a
    /// syntactically valid URL that would otherwise pass every other check.
    /// </summary>
    private const string PlaceholderMarker = "REPLACE_WITH";

    private readonly bool _requireHttps;

    public KeycloakAuthenticationOptionsValidator(IHostEnvironment environment)
        => _requireHttps = !environment.IsDevelopment();

    public ValidateOptionsResult Validate(string? name, KeycloakAuthenticationOptions options)
    {
        var failures = new List<string>();

        ValidateAuthority(options.Authority, failures);
        ValidateRequired(nameof(KeycloakAuthenticationOptions.ClientId), options.ClientId, failures);
        ValidateClientSecret(options.ClientSecret, failures);

        // An http authority is only tolerated in Development, so metadata
        // retrieval must not be downgraded anywhere else either - otherwise a
        // stray RequireHttpsMetadata=false would silently re-open what the
        // authority check above closes.
        if (_requireHttps && !options.RequireHttpsMetadata)
        {
            failures.Add(
                $"{Key(nameof(KeycloakAuthenticationOptions.RequireHttpsMetadata))} must not be disabled outside Development.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateAuthority(string authority, List<string> failures)
    {
        var key = Key(nameof(KeycloakAuthenticationOptions.Authority));

        if (!ValidateRequired(nameof(KeycloakAuthenticationOptions.Authority), authority, failures)
            || !ValidateNotPlaceholder(nameof(KeycloakAuthenticationOptions.Authority), authority, failures))
        {
            return;
        }

        if (PortalUrlPolicy.TryParse(authority, _requireHttps, out _))
        {
            return;
        }

        // Name the actual reason, as PortalRoutingOptionsValidator does: a
        // plain http URL outside Development is an absolute http/https URL, it
        // is just the wrong scheme here.
        failures.Add(_requireHttps && PortalUrlPolicy.TryParse(authority, requireHttps: false, out _)
            ? $"{key} '{authority}' uses http, but https is required outside Development."
            : $"{key} '{authority}' must be an absolute http/https URL, e.g. https://auth.example.com/realms/central-login.");
    }

    private static void ValidateClientSecret(string clientSecret, List<string> failures)
    {
        const string property = nameof(KeycloakAuthenticationOptions.ClientSecret);

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            // The secret is deliberately absent from appsettings, so say where
            // it is meant to come from rather than just "is required".
            failures.Add(
                $"{Key(property)} is required; supply it via user-secrets, the AppHost's " +
                "'central-login-web-client-secret' parameter or the Keycloak__ClientSecret " +
                "environment variable (never in appsettings).");
            return;
        }

        // redactValue: the placeholder test is a substring heuristic, so a real
        // secret that happens to contain the marker would be rejected - and must
        // not then be echoed into the startup log.
        ValidateNotPlaceholder(property, clientSecret, failures, redactValue: true);
    }

    private static bool ValidateRequired(string property, string value, List<string> failures)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        failures.Add($"{Key(property)} is required.");
        return false;
    }

    private static bool ValidateNotPlaceholder(
        string property,
        string value,
        List<string> failures,
        bool redactValue = false)
    {
        if (!value.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var shown = redactValue ? string.Empty : $" ('{value}')";
        failures.Add(
            $"{Key(property)} is still a placeholder value{shown}; " +
            "set it to the real value for this environment.");
        return false;
    }

    private static string Key(string property)
        => $"{KeycloakAuthenticationOptions.SectionName}:{property}";
}
