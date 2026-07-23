namespace CentralLogin.Web.Authentication;

/// <summary>
/// Non-secret OpenID Connect configuration bound from the <c>Keycloak</c>
/// section. <see cref="ClientSecret"/> is the one sensitive value and is
/// supplied at runtime via user-secrets / Aspire parameters / environment — it
/// is never placed in appsettings or committed.
/// </summary>
public sealed class KeycloakAuthenticationOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Realm authority, e.g. <c>https://auth.example.com/realms/central-login</c>.</summary>
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = "central-login-web";

    /// <summary>Confidential client secret. Injected at runtime; keep empty in config files.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Require HTTPS for OIDC metadata/authority. True everywhere except local dev.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    public string CallbackPath { get; set; } = "/signin-oidc";

    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";
}
