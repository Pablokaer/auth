using System.Security.Claims;
using System.Text.Json;
using CentralLogin.Application.Authorization;

namespace CentralLogin.Web.Authentication;

/// <summary>
/// Decides which portal an authenticated account belongs to, based on its
/// Keycloak realm roles. Central login is the single sign-in entry point, but
/// each account is "homed" to exactly one portal: <c>administrator</c> accounts
/// belong to the Admin portal, <c>employee</c> accounts to the Employee portal,
/// and everyone else (the <c>user</c> role) stays on this central app.
///
/// When an account carries several roles, the highest-privilege home wins:
/// administrator &gt; employee &gt; central/user. Reads the realm roles straight
/// from the validated <c>realm_access</c> claim (Keycloak emits it in the id
/// token and userinfo), so it does not depend on the claims transformation
/// having run yet.
/// </summary>
public sealed class PortalRouter
{
    private const string RealmAccessClaim = "realm_access";
    private const string RolesProperty = "roles";

    private readonly PortalRoutingOptions _options;

    public PortalRouter(PortalRoutingOptions options) => _options = options;

    /// <summary>
    /// Returns the absolute base URL of the portal the account should be sent to
    /// after sign-in, or <c>null</c> when the account is homed to this central
    /// app (in which case the normal local return URL should be honoured).
    /// </summary>
    public string? ResolveHomePortalUrl(ClaimsPrincipal principal)
    {
        var roles = ReadRealmRoles(principal);

        if (roles.Contains(ApplicationRoles.Administrator) && !string.IsNullOrWhiteSpace(_options.AdminUrl))
        {
            return _options.AdminUrl;
        }

        if (roles.Contains(ApplicationRoles.Employee) && !string.IsNullOrWhiteSpace(_options.EmployeeUrl))
        {
            return _options.EmployeeUrl;
        }

        // Central / user accounts stay on this app.
        return null;
    }

    private static HashSet<string> ReadRealmRoles(ClaimsPrincipal principal)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        var realmAccess = principal.FindFirst(RealmAccessClaim)?.Value;
        if (string.IsNullOrWhiteSpace(realmAccess))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(realmAccess);
            if (document.RootElement.TryGetProperty(RolesProperty, out var roles)
                && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray())
                {
                    var value = role.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed claim — treat as no realm roles rather than fail sign-in.
        }

        return result;
    }
}
