using Microsoft.Extensions.Options;

namespace CentralLogin.Web.Security;

/// <summary>
/// Fails fast at startup (via <c>ValidateOnStart</c>) when the Data Protection
/// key ring has no durable, shared store configured outside Development.
///
/// Without <see cref="DataProtectionKeyRingOptions.KeyRingPath"/> the key ring
/// is persisted per-user under the OS profile directory (DPAPI-encrypted on
/// Windows, plaintext under <c>~/.aspnet/DataProtection-Keys</c> on Linux/macOS)
/// - a container image typically has no such profile, so the framework falls
/// back to ephemeral, in-memory keys that are regenerated on every start,
/// which silently undecrypts every existing authentication cookie (and the
/// OIDC tokens saved inside it) rather than failing loudly. Development keeps
/// the framework default because restarts there are not a user-facing concern.
/// A configured path must also be absolute: a relative one would resolve
/// against the working directory, which in a container is the writable image
/// layer - the exact failure mode this option exists to prevent.
/// </summary>
public sealed class DataProtectionKeyRingOptionsValidator : IValidateOptions<DataProtectionKeyRingOptions>
{
    private readonly bool _requireKeyRingPath;

    public DataProtectionKeyRingOptionsValidator(IHostEnvironment environment)
        => _requireKeyRingPath = !environment.IsDevelopment();

    public ValidateOptionsResult Validate(string? name, DataProtectionKeyRingOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApplicationName))
        {
            failures.Add($"{Key(nameof(DataProtectionKeyRingOptions.ApplicationName))} is required.");
        }

        if (_requireKeyRingPath)
        {
            if (string.IsNullOrWhiteSpace(options.KeyRingPath))
            {
                failures.Add(
                    $"{Key(nameof(DataProtectionKeyRingOptions.KeyRingPath))} is required outside Development; " +
                    "configure a durable, shared directory (a mounted volume) for the Data Protection key ring, " +
                    "or every restart/redeploy/second instance silently invalidates every existing authentication cookie.");
            }
            else if (!Path.IsPathFullyQualified(options.KeyRingPath))
            {
                // A relative path would resolve against the working directory
                // (see DataProtectionSetup), which in a container is the
                // writable image layer - silently reintroducing the "keys
                // baked into the image" problem this option exists to avoid.
                failures.Add(
                    $"{Key(nameof(DataProtectionKeyRingOptions.KeyRingPath))} '{options.KeyRingPath}' must be an " +
                    "absolute path outside Development; a relative one resolves against the working directory, " +
                    "which in a container is the writable image layer rather than a durable, shared volume.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static string Key(string property)
        => $"{DataProtectionKeyRingOptions.SectionName}:{property}";
}
