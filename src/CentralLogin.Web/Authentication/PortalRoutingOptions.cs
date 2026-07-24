namespace CentralLogin.Web.Authentication;

/// <summary>
/// Absolute base URLs of the per-role portals an account can be "homed" to.
/// After a successful sign-in, an account is bounced to the portal that matches
/// its Keycloak realm role (see <see cref="PortalRouter"/>). Central accounts
/// (the <c>user</c> role) stay on this app and are not listed here.
/// </summary>
public sealed class PortalRoutingOptions
{
    public const string SectionName = "Portals";

    /// <summary>Base URL of the Admin portal (<c>administrator</c> role), e.g. <c>http://localhost:5100</c>.</summary>
    public string? AdminUrl { get; set; }

    /// <summary>Base URL of the Employee portal (<c>employee</c> role), e.g. <c>http://localhost:5001</c>.</summary>
    public string? EmployeeUrl { get; set; }
}
