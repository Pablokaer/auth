using CentralLogin.IntegrationTests.Common;
using CentralLogin.Web.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CentralLogin.IntegrationTests.Configuration;

/// <summary>
/// Guards <c>.ValidateOnStart()</c> + the <see cref="Microsoft.Extensions.Options.IValidateOptions{TOptions}"/>
/// registration in <c>DataProtectionSetup.AddPersistedDataProtection</c>. The
/// environment-dependent "KeyRingPath is required outside Development" branch
/// is covered at the unit level (<see cref="DataProtectionKeyRingOptionsValidatorTests"/>
/// via <c>FakeHostEnvironment</c>) because <see cref="CentralLoginWebFactory"/>
/// always runs as Development; this suite exercises the always-required
/// ApplicationName check instead, which fails regardless of environment.
/// </summary>
public sealed class DataProtectionKeyRingStartupValidationTests
{
    [Fact]
    public void Empty_application_name_fails_startup_naming_the_key()
    {
        using var factory = new CentralLoginWebFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataProtection:ApplicationName"] = "",
                })));

        var thrown = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validationException = StartupValidationFailure.Unwrap(thrown);
        Assert.NotNull(validationException);
        Assert.Contains(validationException!.Failures, f => f.Contains(nameof(DataProtectionKeyRingOptions.ApplicationName), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Valid_configuration_starts_and_the_validated_options_are_what_di_hands_out()
    {
        // CentralLoginWebFactory does not override DataProtection:*, so this
        // is the control case: the stable default application name and no
        // KeyRingPath (fine in Development) must not be rejected at startup.
        using var factory = new CentralLoginWebFactory();

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/");

        Assert.True(response.IsSuccessStatusCode);

        var options = factory.Services.GetRequiredService<DataProtectionKeyRingOptions>();
        Assert.Equal("CentralLogin", options.ApplicationName);
    }
}
