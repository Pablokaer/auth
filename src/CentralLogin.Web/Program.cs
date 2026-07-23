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
builder.Services.AddRazorPages(razor =>
{
    // Home ("/Index") is public. The following pages require authentication:
    razor.Conventions.AuthorizePage("/Dashboard", AuthorizationPolicies.RequireUser);
    razor.Conventions.AuthorizePage("/Admin", AuthorizationPolicies.RequireAdministrator);
});

var app = builder.Build();

// Apply migrations / ensure the SQLite database exists.
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

// Aspire dev endpoints (health/aliveness), development-only by default.
app.MapDefaultEndpoints();

app.Run();

// Exposed so integration tests can reference the entry point via WebApplicationFactory.
public partial class Program;
