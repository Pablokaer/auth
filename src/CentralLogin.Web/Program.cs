using System.Threading.RateLimiting;
using CentralLogin.Application;
using CentralLogin.Application.Authorization;
using CentralLogin.Infrastructure;
using CentralLogin.Web.Authentication;
using CentralLogin.Web.Security;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// --- Aspire service defaults (telemetry, health, resilience, discovery) ------
builder.AddServiceDefaults();

// --- Application composition --------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- Data Protection: stable app name + a durable key ring outside Development,
// so the auth cookie (and its saved tokens) survive a restart/redeploy/second
// instance instead of being silently invalidated ----------------------------
builder.Services.AddPersistedDataProtection(builder.Configuration);

// --- Authentication & authorization ------------------------------------------
builder.Services.AddKeycloakAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddApplicationAuthorization();

// --- Rate limiting on sensitive endpoints ------------------------------------
builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // A conservative fixed window for authentication-initiating requests.
    rateLimiter.AddFixedWindowLimiter(RateLimitingPolicies.Authentication, window =>
    {
        window.PermitLimit = 10;
        window.Window = TimeSpan.FromMinutes(1);
        window.QueueLimit = 0;
    });
});

// --- Razor Pages with page-level authorization -------------------------------
// Authorization is deny-by-default (see the fallback policy in
// AddApplicationAuthorization): any page without authorization metadata now
// requires an authenticated user, so a newly added page is unreachable until
// someone decides otherwise. Public pages must opt out with [AllowAnonymous].
builder.Services.AddRazorPages(razor =>
{
    // Home ("/Index") is public via [AllowAnonymous]. The following pages
    // require the listed policy on top of the fallback's authentication check:
    razor.Conventions.AuthorizePage("/Dashboard", AuthorizationPolicies.RequireUser);
    razor.Conventions.AuthorizePage("/Admin", AuthorizationPolicies.RequireAdministrator);
});

var app = builder.Build();

// Apply migrations / ensure the configured database exists (SQLite or PostgreSQL).
await app.Services.InitializeDatabaseAsync();

// --- HTTP pipeline ------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    // Require HTTPS outside development.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSecurityHeaders();

app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// The fallback authorization policy is also evaluated for requests that match
// no endpoint, so unmatched paths would otherwise challenge anonymous users
// instead of 404-ing. Keep them plain 404s for everyone.
app.MapFallback("/{**path}", () => Results.NotFound()).AllowAnonymous();

// Aspire dev endpoints (health/aliveness), development-only by default.
app.MapDefaultEndpoints();

app.Run();

// Exposed so integration tests can reference the entry point via WebApplicationFactory.
public partial class Program;
