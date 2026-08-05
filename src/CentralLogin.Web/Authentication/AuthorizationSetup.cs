using CentralLogin.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace CentralLogin.Web.Authentication;

public static class AuthorizationSetup
{
    /// <summary>
    /// Registers role-based authorization policies backed by Keycloak roles
    /// (mapped to role claims by <see cref="KeycloakRolesClaimsTransformation"/>),
    /// and a deny-by-default fallback policy so any endpoint without explicit
    /// authorization metadata requires an authenticated user rather than being
    /// implicitly public. Endpoints that carry their own authorization data
    /// (e.g. the <c>AuthorizePage</c> conventions in <c>Program.cs</c>) or an
    /// explicit <see cref="Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute"/>
    /// are unaffected by the fallback policy.
    /// </summary>
    public static IServiceCollection AddApplicationAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.RequireUser, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(ApplicationRoles.User))
            .AddPolicy(AuthorizationPolicies.RequireAdministrator, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(ApplicationRoles.Administrator))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }
}
