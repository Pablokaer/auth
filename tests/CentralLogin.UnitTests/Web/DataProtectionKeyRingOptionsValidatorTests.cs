using CentralLogin.UnitTests.Fakes;
using CentralLogin.Web.Security;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CentralLogin.UnitTests.Web;

public sealed class DataProtectionKeyRingOptionsValidatorTests
{
    // An absolute path on whichever OS the tests run on - Path.IsPathFullyQualified's
    // rules differ across platforms (e.g. "/mnt/keys" is rooted but NOT fully
    // qualified on Windows), so a literal Unix-style path would make this suite
    // platform-dependent.
    private static readonly string AbsoluteKeyRingPath = Path.Combine(Path.GetTempPath(), "centrallogin-keys");

    private static DataProtectionKeyRingOptionsValidator CreateValidator(string environmentName)
        => new(new FakeHostEnvironment(environmentName));

    [Fact]
    public void Default_options_pass_in_development_with_no_key_ring_path()
    {
        var result = CreateValidator(Environments.Development).Validate(name: null, new DataProtectionKeyRingOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_key_ring_path_fails_outside_development()
    {
        var result = CreateValidator(Environments.Production)
            .Validate(name: null, new DataProtectionKeyRingOptions { KeyRingPath = null });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(DataProtectionKeyRingOptions.KeyRingPath), StringComparison.Ordinal));
    }

    [Fact]
    public void Blank_key_ring_path_fails_outside_development()
    {
        var result = CreateValidator(Environments.Production)
            .Validate(name: null, new DataProtectionKeyRingOptions { KeyRingPath = "   " });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(DataProtectionKeyRingOptions.KeyRingPath), StringComparison.Ordinal));
    }

    [Fact]
    public void Configured_key_ring_path_passes_outside_development()
    {
        var result = CreateValidator(Environments.Production)
            .Validate(name: null, new DataProtectionKeyRingOptions { KeyRingPath = AbsoluteKeyRingPath });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Relative_key_ring_path_fails_outside_development()
    {
        // A relative path would resolve against the working directory (see
        // DataProtectionSetup) - in a container that is the writable image
        // layer, silently reintroducing "key material baked into the image".
        var result = CreateValidator(Environments.Production)
            .Validate(name: null, new DataProtectionKeyRingOptions { KeyRingPath = "centrallogin-keys" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(DataProtectionKeyRingOptions.KeyRingPath), StringComparison.Ordinal)
                                               && f.Contains("absolute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Relative_key_ring_path_passes_in_development()
    {
        // Development never requires KeyRingPath at all, so a relative value
        // (e.g. a convenient local folder) must not be rejected there.
        var result = CreateValidator(Environments.Development)
            .Validate(name: null, new DataProtectionKeyRingOptions { KeyRingPath = "centrallogin-keys" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Empty_application_name_fails_regardless_of_environment()
    {
        var result = CreateValidator(Environments.Development)
            .Validate(name: null, new DataProtectionKeyRingOptions { ApplicationName = "", KeyRingPath = AbsoluteKeyRingPath });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(DataProtectionKeyRingOptions.ApplicationName), StringComparison.Ordinal));
    }

    [Fact]
    public void Application_name_stays_at_its_stable_default_unless_overridden()
    {
        // The default is the value AddPersistedDataProtection ends up calling
        // SetApplicationName with when the section is not configured - it must
        // not silently change between releases, or previously-issued cookies
        // become undecryptable.
        Assert.Equal("CentralLogin", new DataProtectionKeyRingOptions().ApplicationName);
    }
}
