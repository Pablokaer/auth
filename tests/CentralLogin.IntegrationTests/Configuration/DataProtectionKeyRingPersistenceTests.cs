using System.Security.Cryptography;
using CentralLogin.IntegrationTests.Common;
using CentralLogin.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CentralLogin.IntegrationTests.Configuration;

/// <summary>
/// Exercises the actual scenario AID-49 is about: a restart (or a second
/// instance) must be able to decrypt a payload - in production, an
/// authentication cookie - produced before it started. Each test builds a
/// fresh, independently composed <see cref="IServiceCollection"/> (standing in
/// for a fresh process) through the same <c>AddPersistedDataProtection</c>
/// entry point Program.cs uses, so this is exercising the real Data
/// Protection subsystem, not a fake.
/// </summary>
public sealed class DataProtectionKeyRingPersistenceTests
{
    [Fact]
    public void A_second_host_pointed_at_the_same_key_ring_path_can_unprotect_the_firsts_payload()
    {
        var keyRingPath = NewKeyRingPath();
        try
        {
            var protectedPayload = CreateProtector(keyRingPath).Protect("session-payload");

            // A second, independently built provider pointed at the same
            // directory simulates the app restarting (or a second instance) -
            // it must decrypt what the "first" provider protected.
            var unprotected = CreateProtector(keyRingPath).Unprotect(protectedPayload);

            Assert.Equal("session-payload", unprotected);
        }
        finally
        {
            DeleteIfExists(keyRingPath);
        }
    }

    [Fact]
    public void Same_key_ring_path_with_different_content_roots_still_unprotects_because_the_application_name_is_explicit()
    {
        // Without an explicit ApplicationName, ASP.NET Core derives the
        // default IApplicationDiscriminator from the content root path, so
        // two hosts with different content roots (e.g. two container images,
        // or two developers' checkouts) would silently derive mismatched
        // purposes and fail to decrypt each other's payload even while
        // sharing the same persisted directory. This test would fail if
        // DataProtectionSetup stopped calling .SetApplicationName(...),
        // because it would then start depending on ContentRootPath instead.
        var keyRingPath = NewKeyRingPath();
        try
        {
            var protectedPayload = CreateProtector(
                keyRingPath,
                contentRootPath: Path.Combine(Path.GetTempPath(), "centrallogin-dp-root-a", Guid.NewGuid().ToString("N")))
                .Protect("session-payload");

            var unprotected = CreateProtector(
                keyRingPath,
                contentRootPath: Path.Combine(Path.GetTempPath(), "centrallogin-dp-root-b", Guid.NewGuid().ToString("N")))
                .Unprotect(protectedPayload);

            Assert.Equal("session-payload", unprotected);
        }
        finally
        {
            DeleteIfExists(keyRingPath);
        }
    }

    [Fact]
    public void Same_key_ring_path_with_different_application_names_cannot_unprotect_each_others_payload()
    {
        // Companion negative for the test above: sharing the physical
        // directory is not enough on its own - a mismatched ApplicationName
        // must still fail to decrypt, because it is folded into the purpose
        // chain used to derive the encryption subkey.
        var keyRingPath = NewKeyRingPath();
        try
        {
            var protectedPayload = CreateProtector(keyRingPath, applicationName: "CentralLogin").Protect("session-payload");

            Assert.Throws<CryptographicException>(
                () => CreateProtector(keyRingPath, applicationName: "SomeOtherApp").Unprotect(protectedPayload));
        }
        finally
        {
            DeleteIfExists(keyRingPath);
        }
    }

    [Fact]
    public void Two_hosts_with_different_key_ring_paths_cannot_unprotect_each_others_payload()
    {
        // Control case for the "same path" test above: two providers that do
        // NOT share a persisted directory (each gets its own, standing in for
        // the pre-fix default where every instance/restart gets its own
        // unpersisted key ring) cannot decrypt each other's payload - exactly
        // how a restart silently invalidates a session cookie without this fix.
        var firstKeyRingPath = NewKeyRingPath();
        var secondKeyRingPath = NewKeyRingPath();
        try
        {
            var protectedPayload = CreateProtector(firstKeyRingPath).Protect("session-payload");

            Assert.Throws<CryptographicException>(() => CreateProtector(secondKeyRingPath).Unprotect(protectedPayload));
        }
        finally
        {
            DeleteIfExists(firstKeyRingPath);
            DeleteIfExists(secondKeyRingPath);
        }
    }

    private static string NewKeyRingPath()
        => Path.Combine(Path.GetTempPath(), "centrallogin-dp-it", Guid.NewGuid().ToString("N"));

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static IDataProtector CreateProtector(
        string? keyRingPath,
        string applicationName = "CentralLogin",
        string? contentRootPath = null)
    {
        var configurationValues = new Dictionary<string, string?>
        {
            [$"{DataProtectionKeyRingOptions.SectionName}:{nameof(DataProtectionKeyRingOptions.ApplicationName)}"] = applicationName,
        };
        if (keyRingPath is not null)
        {
            configurationValues[$"{DataProtectionKeyRingOptions.SectionName}:{nameof(DataProtectionKeyRingOptions.KeyRingPath)}"] = keyRingPath;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configurationValues).Build();

        var environment = new FakeHostEnvironment(Environments.Development);
        if (contentRootPath is not null)
        {
            environment.ContentRootPath = contentRootPath;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddPersistedDataProtection(configuration);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("CentralLogin.Auth");
    }
}
