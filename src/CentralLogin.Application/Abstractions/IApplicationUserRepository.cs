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

    Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
