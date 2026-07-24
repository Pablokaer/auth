namespace CentralLogin.Application.Authorization;

/// <summary>
/// Canonical role names as configured in the Keycloak <c>central-login</c> realm.
/// These are the realm role names; the claims transformation in the Web layer
/// maps Keycloak realm/client roles onto ASP.NET Core role claims using these.
/// </summary>
public static class ApplicationRoles
{
    public const string User = "user";
    public const string Administrator = "administrator";
    public const string Employee = "employee";
}
