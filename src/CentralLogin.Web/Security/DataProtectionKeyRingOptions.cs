namespace CentralLogin.Web.Security;

/// <summary>
/// Configuration for the ASP.NET Core Data Protection key ring, bound from the
/// <c>DataProtection</c> section. The key ring is what encrypts the
/// authentication cookie - and, because <c>AuthenticationSetup</c> sets
/// <c>SaveTokens = true</c>, the OIDC id/access tokens carried inside it - so
/// without a stable name and a durable, shared store every
/// restart/redeploy/second instance regenerates the keys and silently signs
/// every user out (see <see cref="DataProtectionKeyRingOptionsValidator"/>).
/// </summary>
public sealed class DataProtectionKeyRingOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Stable identity embedded in every payload the key ring protects. Must
    /// stay the same across deployments and instances of this app - changing
    /// it makes previously-issued cookies undecryptable even if the key ring
    /// itself is preserved.
    /// </summary>
    public string ApplicationName { get; set; } = "CentralLogin";

    /// <summary>
    /// Directory the key ring is persisted to via
    /// <c>PersistKeysToFileSystem</c>. Outside Development this must be a
    /// durable location shared by every instance - typically a mounted volume
    /// - so a restart does not lose the keys and a second instance can
    /// decrypt cookies the first one issued. Must be an absolute path when
    /// required (outside Development) - a relative path would resolve
    /// against the working directory, which in a container is the writable
    /// image layer, exactly what this option exists to avoid. Left unset in
    /// Development, where the framework's default (persisted per-user under
    /// the user profile directory) is fine.
    /// </summary>
    public string? KeyRingPath { get; set; }
}
