using System.Security.Claims;
using System.Text.Json;

namespace CentralLogin.Web.Authentication;

/// <summary>
/// Claims transformation that projects Keycloak realm roles and
/// client roles
/// onto standard ASP.NET Core role claims (<see cref="ClaimTypes.Role"/>), so
/// <c>[Authorize(Roles = ...)]</c> and role-based policies work as usual.
///
/// Keycloak emits roles as nested JSON claims:
/// <code>
///   realm_access    = { "roles": ["user","administrator"] }
///   resource_access = { "central-login-web": { "roles": ["administrator"] } }
/// </code>
/// This reads them from the already-validated principal. It does NOT parse or
/// validate token signatures — the OpenID Connect middleware has already done
/// that. The transformation is idempotent (safe to run on every request).
/// </summary>
public sealed class KeycloakRolesClaimsTransformation : Microsoft.AspNetCore.Authentication.IClaimsTransformation
{
    private const string RealmAccessClaim = "realm_access";
    private const string ResourceAccessClaim = "resource_access";
    private const string RolesProperty = "roles";

    private readonly KeycloakAuthenticationOptions _options;

    public KeycloakRolesClaimsTransformation(KeycloakAuthenticationOptions options) => _options = options;

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        var discovered = new HashSet<string>(StringComparer.Ordinal);

        CollectRealmRoles(principal, discovered);
        CollectClientRoles(principal, discovered, _options.ClientId);

        foreach (var role in discovered)
        {
            if (!identity.HasClaim(ClaimTypes.Role, role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }

        return Task.FromResult(principal);
    }

    private static void CollectRealmRoles(ClaimsPrincipal principal, HashSet<string> target)
    {
        var realmAccess = principal.FindFirst(RealmAccessClaim)?.Value;
        if (string.IsNullOrWhiteSpace(realmAccess))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(realmAccess);
            AddRolesFromArray(document.RootElement, target);
        }
        catch (JsonException)
        {
            // Malformed claim — ignore rather than fail the request.
        }
    }

    private static void CollectClientRoles(ClaimsPrincipal principal, HashSet<string> target, string clientId)
    {
        var resourceAccess = principal.FindFirst(ResourceAccessClaim)?.Value;
        if (string.IsNullOrWhiteSpace(resourceAccess) || string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(resourceAccess);
            if (document.RootElement.TryGetProperty(clientId, out var client))
            {
                AddRolesFromArray(client, target);
            }
        }
        catch (JsonException)
        {
            // Malformed claim — ignore.
        }
    }

    private static void AddRolesFromArray(JsonElement container, HashSet<string> target)
    {
        if (container.ValueKind == JsonValueKind.Object
            && container.TryGetProperty(RolesProperty, out var roles)
            && roles.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in roles.EnumerateArray())
            {
                var value = role.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    target.Add(value);
                }
            }
        }
    }
}
