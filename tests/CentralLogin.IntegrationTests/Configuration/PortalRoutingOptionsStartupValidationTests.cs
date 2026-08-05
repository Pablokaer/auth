using CentralLogin.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace CentralLogin.IntegrationTests.Configuration;

/// <summary>
/// Guards <c>.ValidateOnStart()</c> + the <see cref="IValidateOptions{TOptions}"/>
/// registration in <c>AuthenticationSetup.AddKeycloakAuthentication</c>: nothing
/// else in this suite would fail if either were removed, because
/// <see cref="CentralLogin.Web.Authentication.PortalRoutingOptions"/> is never
/// otherwise exercised at startup.
/// </summary>
public sealed class PortalRoutingOptionsStartupValidationTests
{
    [Fact]
    public void Invalid_admin_url_fails_startup_with_an_options_validation_exception()
    {
        // CentralLoginWebFactory runs as Development (see ConfigureWebHost), so
        // this must be a value that PortalUrlPolicy rejects regardless of the
        // requireHttps flag - "not a url" fails absolute-URI parsing outright,
        // so the test does not depend on which environment applies.
        using var factory = new CentralLoginWebFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Portals:AdminUrl"] = "not a url",
                })));

        var thrown = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validationException = StartupValidationFailure.Unwrap(thrown);
        Assert.NotNull(validationException);
        Assert.Contains(validationException!.Failures, f => f.Contains("AdminUrl", StringComparison.Ordinal));
    }
}
