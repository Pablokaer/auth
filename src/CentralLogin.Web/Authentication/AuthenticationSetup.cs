using System.Security.Claims;
using CentralLogin.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    ///
    /// Both configuration sections it reads are validated at startup
    /// (<c>ValidateOnStart</c>), so a misconfigured deployment fails while the
    /// host is starting instead of at a user's first sign-in.
    /// </summary>
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Bound through the options system (rather than a one-shot Get<T>() with
        // an `?? new()` fallback, which happily accepted a missing section) so
        // KeycloakAuthenticationOptionsValidator + ValidateOnStart abort startup
        // on a deployment that never overrode the appsettings placeholders. The
        // plain singleton is resolved from IOptions<T>.Value, so consumers such
        // as KeycloakRolesClaimsTransformation cannot obtain an unvalidated
        // instance from DI.
        services.AddOptions<KeycloakAuthenticationOptions>()
            .Bind(configuration.GetSection(KeycloakAuthenticationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<KeycloakAuthenticationOptions>, KeycloakAuthenticationOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<KeycloakAuthenticationOptions>>().Value);

        var isDevelopment = environment.IsDevelopment();

        // Bind PortalRoutingOptions through the options system (rather than a
        // one-shot Get<T>()) so IValidateOptions + ValidateOnStart can fail
        // fast at startup on a misconfigured portal URL (see
        // PortalRoutingOptionsValidator / PortalUrlPolicy). PortalRouter still
        // takes a plain PortalRoutingOptions singleton — resolved from
        // IOptions<PortalRoutingOptions>.Value below — so that registration,
        // and therefore validation, is what actually runs at startup.
        services.AddOptions<PortalRoutingOptions>()
            .Bind(configuration.GetSection(PortalRoutingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PortalRoutingOptions>, PortalRoutingOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PortalRoutingOptions>>().Value);

        // requireHttps mirrors the validator: https is required outside
        // Development, so an already-invalid-in-Development-only URL never
        // becomes a redirect target once promoted to a non-dev environment.
        services.AddSingleton(sp => new PortalRouter(
            sp.GetRequiredService<PortalRoutingOptions>(),
            sp.GetRequiredService<ILogger<PortalRouter>>(),
            requireHttps: !isDevelopment));

        services.AddTransient<IClaimsTransformation, KeycloakRolesClaimsTransformation>();

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
        .AddOpenIdConnect();

        // Configured from the validated KeycloakAuthenticationOptions instead of
        // a value captured here, so the handler can only ever see settings that
        // passed KeycloakAuthenticationOptionsValidator.
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<KeycloakAuthenticationOptions>(ConfigureOpenIdConnect);

        return services;
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions oidc, KeycloakAuthenticationOptions options)
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

            // Never surface raw error details (which may include codes) to
            // the browser; redirect to a generic error page. The failure is
            // still logged server-side - see HandleRemoteFailure.
            OnRemoteFailure = HandleRemoteFailure,
        };
    }

    /// <summary>
    /// Records the reason a remote OIDC exchange failed (exception type and
    /// message - which, for a provider-reported failure, already folds in
    /// the OIDC <c>error</c>/<c>error_description</c>) before redirecting to
    /// the generic error page. A named method rather than an inline lambda
    /// so a test can invoke it directly with a constructed
    /// <see cref="RemoteFailureContext"/>.
    /// </summary>
    public static Task HandleRemoteFailure(RemoteFailureContext context)
    {
        // Redirect + HandleResponse() first and unconditionally: the original
        // lambda could not throw, so a throwing logger provider must not turn
        // a handled remote failure into an unhandled 500 - which, on a
        // Development host, would surface the provider's error detail via the
        // exception page instead of the generic one.
        context.Response.Redirect("/Error?source=auth");
        context.HandleResponse();

        context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AuthenticationSetup).FullName!)
            .LogWarning(context.Failure, "OIDC remote authentication failed.");

        return Task.CompletedTask;
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
