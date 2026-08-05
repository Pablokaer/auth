using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CentralLogin.IntegrationTests.Common;

/// <summary>
/// Registers a minimal endpoint that exists only in the test host and carries
/// no authorization metadata whatsoever — no <c>[Authorize]</c>, no
/// <c>AuthorizePage</c> convention, no <c>[AllowAnonymous]</c>. Production
/// pages all already carry one or the other, so they cannot prove the
/// deny-by-default fallback authorization policy (see
/// <c>AddApplicationAuthorization</c>) actually does anything; this endpoint
/// can. It is added via <see cref="IStartupFilter"/> so it is wired into the
/// same pipeline/routing instance the app builds in <c>Program.cs</c>,
/// including <c>UseAuthorization</c>.
/// </summary>
public sealed class TestOnlyUnprotectedEndpointStartupFilter : IStartupFilter
{
    public const string Path = "/test-only/no-authorization-metadata";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            next(app);

            app.UseEndpoints(endpoints => endpoints.MapGet(Path, () => Results.Ok("reached")));
        };
}
