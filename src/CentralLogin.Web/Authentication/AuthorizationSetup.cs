using CentralLogin.Application.Authorization;

namespace CentralLogin.Web.Authentication;

public static class AuthorizationSetup
{
    /// <summary>
    /// Registers role-based authorization policies backed by Keycloak roles
    /// (mapped to role claims by <see cref="KeycloakRolesClaimsTransformation"/>).
    /// </summary>
    public static IServiceCollection AddApplicationAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.RequireUser, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(ApplicationRoles.User))
            .AddPolicy(AuthorizationPolicies.RequireAdministrator, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(ApplicationRoles.Administrator));

        return services;
    }
}
