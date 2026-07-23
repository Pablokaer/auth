namespace CentralLogin.Web.Security;

public static class RateLimitingPolicies
{
    /// <summary>Applied to authentication-initiating endpoints (e.g. login).</summary>
    public const string Authentication = "auth";
}
