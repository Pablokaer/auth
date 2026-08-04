using System.Security.Claims;
using System.Text.Json;
using CentralLogin.Application.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger<PortalRouter> _logger;
    private readonly bool _requireHttps;

    /// <param name="options">The configured portal URLs.</param>
    /// <param name="logger">
    /// Optional; defaults to <see cref="NullLogger{T}"/> so unit tests can
    /// construct a <see cref="PortalRouter"/> directly without a DI-provided
    /// logger. Production code should always pass a real one.
    /// </param>
    /// <param name="requireHttps">
    /// Optional; defaults to <c>false</c> so existing tests that use
    /// <c>http://localhost</c> portal URLs keep passing. Production wiring
    /// passes <c>!environment.IsDevelopment()</c>.
    /// </param>
    public PortalRouter(PortalRoutingOptions options, ILogger<PortalRouter>? logger = null, bool requireHttps = false)
    {
        _options = options;
        _logger = logger ?? NullLogger<PortalRouter>.Instance;
        _requireHttps = requireHttps;
    }

    /// <summary>
    /// Returns the absolute base URL of the portal the account should be sent to
    /// after sign-in, or <c>null</c> when the account is homed to this central
    /// app (in which case the normal local return URL should be honoured).
    ///
    /// A configured portal URL that fails <see cref="PortalUrlPolicy"/> (not an
    /// absolute http/https URL, or plain http when https is required) is never
    /// returned — the account falls through to the local return URL instead,
    /// and the rejection is logged as a warning naming the portal.
    /// </summary>
    public string? ResolveHomePortalUrl(ClaimsPrincipal principal)
    {
        var roles = ReadRealmRoles(principal);

        if (roles.Contains(ApplicationRoles.Administrator))
        {
            // Deliberate: this short-circuits on role alone. A principal that
            // is BOTH administrator and employee, with an unset/invalid
            // AdminUrl, falls through to the local return URL (stays on
            // central) rather than falling through further to the employee
            // portal - an administrator should never be silently handed the
            // lower-privilege employee portal as a consolation destination.
            return ValidatedPortalUrl("Admin", _options.AdminUrl);
        }

        if (roles.Contains(ApplicationRoles.Employee))
        {
            return ValidatedPortalUrl("Employee", _options.EmployeeUrl);
        }

        // Central / user accounts stay on this app.
        return null;
    }

    /// <summary>
    /// Returns the parsed, normalised (<see cref="Uri.AbsoluteUri"/>) form of
    /// <paramref name="candidate"/> when it is configured and passes
    /// <see cref="PortalUrlPolicy"/>; otherwise <c>null</c>. Returning the
    /// normalised URI - rather than the raw <paramref name="candidate"/>
    /// string - matters because <see cref="Uri.TryCreate(string?, UriKind, out Uri?)"/>
    /// tolerates surrounding whitespace/control characters that must never
    /// reach <c>context.ReturnUri</c> (and therefore the <c>Location</c>
    /// header) verbatim. An unconfigured (null/empty) portal is not a warning
    /// - that is the normal "stays on central" case - but a
    /// configured-yet-invalid one is, since it points at an operator mistake
    /// that would otherwise silently misroute logins.
    /// </summary>
    private string? ValidatedPortalUrl(string portalName, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (!PortalUrlPolicy.TryParse(candidate, _requireHttps, out var uri))
        {
            _logger.LogWarning(
                "Rejected {Portal} portal URL '{PortalUrl}' from PortalRoutingOptions: {Reason}. Falling back to the local return URL.",
                portalName,
                candidate,
                RejectionReason(candidate));
            return null;
        }

        return uri!.AbsoluteUri;
    }

    /// <summary>
    /// Names the actual reason <paramref name="candidate"/> was rejected, so
    /// the logged warning does not claim "not an absolute http/https URL"
    /// for a value that is in fact an absolute http URL simply rejected
    /// because https is required outside Development.
    /// </summary>
    private string RejectionReason(string candidate) =>
        _requireHttps && PortalUrlPolicy.TryParse(candidate, requireHttps: false, out _)
            ? "http is not permitted outside Development; https is required"
            : "not an absolute http/https URL";

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
