using System.Security.Claims;
using CentralLogin.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CentralLogin.Web.Authentication;

public static class AuthenticationSetup
{
    /// <summary>
    /// Configures cookie + OpenID Connect authentication against Keycloak using
    /// the Authorization Code flow with PKCE. State/nonce validation, issuer,
    /// audience, lifetime and signing-key validation are all performed by the
    /// standard ASP.NET Core handler.
    /// </summary>
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration
            .GetSection(KeycloakAuthenticationOptions.SectionName)
            .Get<KeycloakAuthenticationOptions>() ?? new KeycloakAuthenticationOptions();

        var portalOptions = configuration
            .GetSection(PortalRoutingOptions.SectionName)
            .Get<PortalRoutingOptions>() ?? new PortalRoutingOptions();

        services.AddSingleton(options);
        services.AddSingleton(portalOptions);
        services.AddSingleton<PortalRouter>();
        services.AddTransient<IClaimsTransformation, KeycloakRolesClaimsTransformation>();

        var isDevelopment = environment.IsDevelopment();

        services.AddAuthentication(auth =>
        {
            auth.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            auth.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(cookie =>
        {
            // __Host- prefix enforces Secure + Path=/ + no Domain; only valid
            // over HTTPS, so use a plain name in local (HTTP) development.
            cookie.Cookie.Name = isDevelopment ? "CentralLogin.Auth" : "__Host-CentralLogin.Auth";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = isDevelopment
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            cookie.SlidingExpiration = true;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.LoginPath = "/Account/Login";
            cookie.LogoutPath = "/Account/Logout";
            cookie.AccessDeniedPath = "/Account/AccessDenied";
        })
        .AddOpenIdConnect(oidc =>
        {
            oidc.Authority = options.Authority;
            oidc.ClientId = options.ClientId;
            oidc.ClientSecret = options.ClientSecret;
            oidc.RequireHttpsMetadata = options.RequireHttpsMetadata;

            // Authorization Code flow + PKCE (confidential client).
            oidc.ResponseType = OpenIdConnectResponseType.Code;
            oidc.UsePkce = true;
            oidc.ResponseMode = OpenIdConnectResponseMode.Query;

            oidc.SaveTokens = true;                        // enables id_token_hint on logout
            oidc.GetClaimsFromUserInfoEndpoint = true;
            oidc.MapInboundClaims = false;                 // keep original claim names (sub, realm_access, ...)

            oidc.CallbackPath = options.CallbackPath;
            oidc.SignedOutCallbackPath = options.SignedOutCallbackPath;

            oidc.Scope.Clear();
            oidc.Scope.Add(OpenIdConnectScope.OpenId);
            oidc.Scope.Add("profile");
            oidc.Scope.Add("email");
            oidc.Scope.Add("roles");

            // Explicit, defence-in-depth validation (these are also defaults).
            oidc.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "preferred_username",
                RoleClaimType = ClaimTypes.Role,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            oidc.Events = new OpenIdConnectEvents
            {
                // Provision/update the local user right after the token is validated.
                OnTokenValidated = ProvisionLocalUserAsync,

                // Home each account to its portal: administrator -> Admin (5100),
                // employee -> Employee (5001), everyone else stays on central (5080).
                OnTicketReceived = RouteToHomePortal,

                // Never surface raw error details (which may include codes) to the
                // browser or logs; redirect to a generic error page.
                OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/Error?source=auth");
                    context.HandleResponse();
                    return Task.CompletedTask;
                },
            };
        });

        return services;
    }

    private static async Task ProvisionLocalUserAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null)
        {
            return;
        }

        var subject = principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(subject))
        {
            return;
        }

        var displayName = principal.FindFirst("name")?.Value
                          ?? principal.FindFirst("preferred_username")?.Value;
        var email = principal.FindFirst("email")?.Value;

        try
        {
            var provisioning = context.HttpContext.RequestServices
                .GetRequiredService<IUserProvisioningService>();

            await provisioning.ProvisionOrUpdateAsync(
                new ProvisionUserCommand(subject, displayName, email),
                context.HttpContext.RequestAborted);
        }
        // Deliberately NOT caught here: an OperationCanceledException (e.g. the
        // request was aborted) is left to propagate rather than logged as a
        // provisioning failure - there is no meaningful outcome to report for a
        // request that no longer exists.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Explicit decision: a provisioning failure does NOT block sign-in.
            // OnRemoteFailure only covers protocol-level OIDC failures, not
            // exceptions thrown from OnTokenValidated - letting one through here
            // would surface as an unhandled 500 on the user's very first sign-in
            // for an unrelated (e.g. database) problem. The local user projection
            // is best-effort and self-heals on the next login, so we log and let
            // the already-validated identity proceed. Never log the raw subject;
            // the hash lets an operator correlate this with provisioning logs.
            context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AuthenticationSetup).FullName!)
                .LogError(ex, "Local user provisioning failed for subject {SubjectHash}.", SubjectLogHash.Compute(subject));
        }
    }

    /// <summary>
    /// After a successful sign-in, redirects the account to its home portal
    /// based on realm role. Central/user accounts fall through to their normal
    /// local return URL; employee and administrator accounts are bounced to the
    /// dedicated portal, which completes its own login silently via the shared
    /// Keycloak SSO session. The role decides the destination regardless of
    /// where the sign-in was initiated from.
    /// </summary>
    private static Task RouteToHomePortal(TicketReceivedContext context)
    {
        if (context.Principal is null)
        {
            return Task.CompletedTask;
        }

        var router = context.HttpContext.RequestServices.GetRequiredService<PortalRouter>();
        var portalUrl = router.ResolveHomePortalUrl(context.Principal);

        if (!string.IsNullOrWhiteSpace(portalUrl))
        {
            context.ReturnUri = portalUrl;
        }

        return Task.CompletedTask;
    }
}
