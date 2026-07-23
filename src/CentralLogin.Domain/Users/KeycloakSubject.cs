using CentralLogin.Domain.Common;

namespace CentralLogin.Domain.Users;

/// <summary>
/// Value object wrapping the Keycloak <c>sub</c> (subject) claim.
///
/// This is the PERMANENT external identity identifier for a local user. It is
/// deliberately NOT the email address: emails can change and be reused, whereas
/// the subject is stable for the lifetime of the Keycloak user account.
/// </summary>
public sealed record KeycloakSubject
{
    public string Value { get; }

    private KeycloakSubject(string value) => Value = value;

    public static KeycloakSubject Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Keycloak subject must be a non-empty value.");
        }

        return new KeycloakSubject(value.Trim());
    }

    public override string ToString() => Value;
}
