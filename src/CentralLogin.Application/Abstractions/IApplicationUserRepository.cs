using CentralLogin.Domain.Users;

namespace CentralLogin.Application.Abstractions;

/// <summary>
/// Persistence abstraction for <see cref="ApplicationUser"/>. Implemented in the
/// Infrastructure layer (EF Core + SQLite). Declared here so the Application
/// layer depends on an interface, not on a database technology.
/// </summary>
public interface IApplicationUserRepository
{
    Task<ApplicationUser?> GetBySubjectAsync(KeycloakSubject subject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plain insert, with no protection against a concurrent unique-index
    /// collision on <see cref="KeycloakSubject"/>. Provisioning a possibly-new
    /// user (first login) MUST go through <see cref="TryAddAsync"/> instead -
    /// using this for that purpose reintroduces the AID-43 race. Only safe when
    /// the caller already holds an exclusive claim on the subject (e.g. a
    /// migration/seed script).
    /// </summary>
    Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to insert a brand-new user and commit it in one step. If a
    /// concurrent caller has already committed a user for the same
    /// <see cref="KeycloakSubject"/> (unique-index collision), this reports
    /// <see cref="UserAddOutcome.AlreadyExists"/> instead of throwing, so first-login
    /// races can fall through to the existing-user path. Any other persistence
    /// failure still propagates.
    /// </summary>
    Task<UserAddOutcome> TryAddAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IApplicationUserRepository.TryAddAsync"/>.</summary>
public enum UserAddOutcome
{
    Added,
    AlreadyExists,
}
