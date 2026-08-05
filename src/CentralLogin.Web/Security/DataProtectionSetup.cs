using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace CentralLogin.Web.Security;

/// <summary>
/// Configures the ASP.NET Core Data Protection key ring with a stable
/// application name and, when configured, a durable file-system store. See
/// <see cref="DataProtectionKeyRingOptions"/> and
/// <see cref="DataProtectionKeyRingOptionsValidator"/> for why an unpersisted
/// key ring silently invalidates every authentication cookie on restart.
/// </summary>
public static class DataProtectionSetup
{
    public static IServiceCollection AddPersistedDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bound through the options system (rather than a one-shot Get<T>())
        // so DataProtectionKeyRingOptionsValidator + ValidateOnStart abort
        // startup outside Development when no durable key store is
        // configured - ValidateOnStart is what enforces this, independent of
        // whether anything resolves the value below. The plain singleton
        // mirrors AddKeycloakAuthentication's pattern purely for convenience:
        // any future consumer can inject DataProtectionKeyRingOptions
        // directly instead of unwrapping IOptions<T>.
        services.AddOptions<DataProtectionKeyRingOptions>()
            .Bind(configuration.GetSection(DataProtectionKeyRingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DataProtectionKeyRingOptions>, DataProtectionKeyRingOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DataProtectionKeyRingOptions>>().Value);

        // AddDataProtection()'s builder (SetApplicationName,
        // PersistKeysToFileSystem) configures the key ring immediately, at
        // service-registration time, so the section is bound here too. This
        // does not bypass the validation above: ValidateOnStart still fails
        // the host at startup if the value used below is missing/invalid -
        // this bind only decides what an already-valid (or Development)
        // configuration is set up with.
        var options = configuration.GetSection(DataProtectionKeyRingOptions.SectionName)
            .Get<DataProtectionKeyRingOptions>() ?? new DataProtectionKeyRingOptions();

        var dataProtection = services.AddDataProtection()
            .SetApplicationName(options.ApplicationName);

        if (!string.IsNullOrWhiteSpace(options.KeyRingPath))
        {
            var directory = CreateOwnerOnlyDirectory(Path.GetFullPath(options.KeyRingPath));
            dataProtection.PersistKeysToFileSystem(directory);
        }
        // Else: no KeyRingPath configured. Outside Development the validator
        // above has already failed startup, so reaching here without one
        // means the framework's per-user default key ring is acceptable
        // (Development only).

        return services;
    }

    /// <summary>
    /// Creates (or reuses) the key ring directory, restricted to the owning
    /// user only on Unix (0700 - <c>Directory.CreateDirectory</c> is a no-op
    /// when the directory already exists, so this never fights a pre-mounted
    /// volume's own permissions). The key XML written here is not encrypted
    /// at rest unless a certificate/KMS encryptor is layered on top (see the
    /// README), so a world/group-readable directory would let any other
    /// local user read the keys that decrypt every session cookie. The
    /// <c>UnixFileMode</c> overload throws <see cref="PlatformNotSupportedException"/>
    /// on Windows, hence the OS check.
    /// </summary>
    private static DirectoryInfo CreateOwnerOnlyDirectory(string path)
        => OperatingSystem.IsWindows()
            ? Directory.CreateDirectory(path)
            : Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}
