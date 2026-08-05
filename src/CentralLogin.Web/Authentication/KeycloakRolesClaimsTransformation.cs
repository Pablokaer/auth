using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger<KeycloakRolesClaimsTransformation> _logger;

    /// <param name="options">The Keycloak client configuration.</param>
    /// <param name="logger">
    /// Optional; defaults to <see cref="NullLogger{T}"/> so unit tests can
    /// construct a <see cref="KeycloakRolesClaimsTransformation"/> directly
    /// without a DI-provided logger. The existing
    /// <c>AddTransient&lt;IClaimsTransformation, KeycloakRolesClaimsTransformation&gt;()</c>
    /// registration still resolves a real <see cref="ILogger{TCategoryName}"/>
    /// through DI in production.
    /// </param>
    public KeycloakRolesClaimsTransformation(
        KeycloakAuthenticationOptions options,
        ILogger<KeycloakRolesClaimsTransformation>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<KeycloakRolesClaimsTransformation>.Instance;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        var discovered = new HashSet<string>(StringComparer.Ordinal);

        CollectRealmRoles(principal, discovered, _logger);
        CollectClientRoles(principal, discovered, _options.ClientId, _logger);

        foreach (var role in discovered)
        {
            if (!identity.HasClaim(ClaimTypes.Role, role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }

        return Task.FromResult(principal);
    }

    private static void CollectRealmRoles(ClaimsPrincipal principal, HashSet<string> target, ILogger logger)
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
        catch (JsonException ex)
        {
            // Malformed claim — ignore rather than fail the request.
            LogMalformedClaim(logger, RealmAccessClaim, realmAccess.Length, ex);
        }
    }

    private static void CollectClientRoles(ClaimsPrincipal principal, HashSet<string> target, string clientId, ILogger logger)
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
        catch (JsonException ex)
        {
            // Malformed claim — ignore.
            LogMalformedClaim(logger, ResourceAccessClaim, resourceAccess.Length, ex);
        }
    }

    /// <summary>
    /// Logs a warning naming the claim that failed to parse as JSON, without
    /// the raw claim value or any token material: only the claim name, the
    /// claim value's length, and the exception's parse position
    /// (<see cref="JsonException.LineNumber"/> / <see cref="JsonException.BytePositionInLine"/>)
    /// — enough to distinguish a truncated claim from mid-value corruption.
    /// The <see cref="JsonException"/> itself is deliberately not passed to
    /// the logger: <see cref="JsonException.Message"/> embeds the unparsed
    /// input tail verbatim and unbounded, which would leak claim content (and
    /// could inject forged log lines via control characters) into the log.
    /// </summary>
    private static void LogMalformedClaim(ILogger logger, string claimType, int claimValueLength, JsonException ex) =>
        logger.LogWarning(
            "Failed to parse Keycloak '{ClaimType}' claim as JSON; treating as no roles from this claim. " +
            "ValueLength={ValueLength} Line={Line} BytePositionInLine={BytePositionInLine} ExceptionType={ExceptionType}",
            claimType,
            claimValueLength,
            ex.LineNumber,
            ex.BytePositionInLine,
            ex.GetType().Name);

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
