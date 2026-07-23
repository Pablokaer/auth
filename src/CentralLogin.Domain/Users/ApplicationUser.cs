namespace CentralLogin.Domain.Users;

/// <summary>
/// Local projection of an authenticated identity. This is application-specific
/// data only. It intentionally stores NO credentials, password hashes, refresh
/// tokens, client secrets or other Keycloak-internal data — those live in
/// Keycloak. The link to the identity provider is <see cref="KeycloakSubject"/>.
/// </summary>
public sealed class ApplicationUser
{
    public Guid Id { get; private set; }

    /// <summary>Permanent external identity key (Keycloak <c>sub</c>). Immutable.</summary>
    public KeycloakSubject KeycloakSubject { get; private set; }

    /// <summary>Optional friendly name captured from claims. May change over time.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>
    /// Optional, non-authoritative snapshot of the email at last login.
    /// Kept for display/convenience only; it is NEVER used as the identity key.
    /// </summary>
    public string? EmailSnapshot { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime LastLoginAtUtc { get; private set; }

    // Required by EF Core; not for application use.
    private ApplicationUser() => KeycloakSubject = null!;

    private ApplicationUser(
        Guid id,
        KeycloakSubject keycloakSubject,
        string? displayName,
        string? emailSnapshot,
        DateTime utcNow)
    {
        Id = id;
        KeycloakSubject = keycloakSubject;
        DisplayName = Normalize(displayName);
        EmailSnapshot = Normalize(emailSnapshot);
        CreatedAtUtc = utcNow;
        LastLoginAtUtc = utcNow;
    }

    /// <summary>Creates a brand new local user on first successful authentication.</summary>
    public static ApplicationUser Provision(
        KeycloakSubject keycloakSubject,
        string? displayName,
        string? emailSnapshot,
        DateTime utcNow)
        => new(Guid.NewGuid(), keycloakSubject, displayName, emailSnapshot, utcNow);

    /// <summary>
    /// Updates the mutable snapshot fields and records the login time. The
    /// Keycloak subject is never changed here — identity is permanent.
    /// </summary>
    public void RecordLogin(string? displayName, string? emailSnapshot, DateTime utcNow)
    {
        DisplayName = Normalize(displayName) ?? DisplayName;
        EmailSnapshot = Normalize(emailSnapshot) ?? EmailSnapshot;
        LastLoginAtUtc = utcNow;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
