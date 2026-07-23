namespace CentralLogin.Application.Users;

/// <summary>
/// Provisions a local <c>ApplicationUser</c> on first login and updates the
/// mutable snapshot fields on subsequent logins, keyed by the Keycloak subject.
/// </summary>
public interface IUserProvisioningService
{
    Task<ApplicationUserResult> ProvisionOrUpdateAsync(
        ProvisionUserCommand command,
        CancellationToken cancellationToken = default);
}
