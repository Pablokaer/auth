using CentralLogin.Domain.Users;

namespace CentralLogin.Application.Users;

/// <summary>Read-model returned by the provisioning use case.</summary>
public sealed record ApplicationUserResult(
    Guid Id,
    string Subject,
    string? DisplayName,
    string? Email,
    DateTime CreatedAtUtc,
    DateTime LastLoginAtUtc,
    bool WasCreated)
{
    public static ApplicationUserResult From(ApplicationUser user, bool wasCreated) => new(
        user.Id,
        user.KeycloakSubject.Value,
        user.DisplayName,
        user.EmailSnapshot,
        user.CreatedAtUtc,
        user.LastLoginAtUtc,
        wasCreated);
}
