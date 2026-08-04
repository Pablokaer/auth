using CentralLogin.Application.Abstractions;
using CentralLogin.Domain.Users;

namespace CentralLogin.UnitTests.Fakes;

/// <summary>Simple in-memory repository so provisioning can be unit-tested
/// without a database or a real Keycloak server.</summary>
public sealed class InMemoryApplicationUserRepository : IApplicationUserRepository
{
    private readonly List<ApplicationUser> _users = [];

    public int SaveChangesCallCount { get; private set; }

    public IReadOnlyList<ApplicationUser> Users => _users;

    public Task<ApplicationUser?> GetBySubjectAsync(KeycloakSubject subject, CancellationToken cancellationToken = default)
        => Task.FromResult(_users.FirstOrDefault(u => u.KeycloakSubject.Value == subject.Value));

    public Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }

    public Task<UserAddOutcome> TryAddAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (_users.Any(u => u.KeycloakSubject.Value == user.KeycloakSubject.Value))
        {
            return Task.FromResult(UserAddOutcome.AlreadyExists);
        }

        _users.Add(user);
        SaveChangesCallCount++;
        return Task.FromResult(UserAddOutcome.Added);
    }
}
