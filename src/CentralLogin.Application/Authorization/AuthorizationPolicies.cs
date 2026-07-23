namespace CentralLogin.Application.Authorization;

/// <summary>
/// Names of the ASP.NET Core authorization policies. Centralised here so both
/// the policy registration (Web) and the <c>[Authorize(Policy = ...)]</c> usages
/// reference the same constants.
/// </summary>
public static class AuthorizationPolicies
{
    public const string RequireUser = "RequireUser";
    public const string RequireAdministrator = "RequireAdministrator";
}
