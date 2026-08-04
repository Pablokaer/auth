using CentralLogin.Application.Abstractions;
using CentralLogin.Domain.Users;

namespace CentralLogin.UnitTests.Fakes;

/// <summary>
/// Test double that simulates having lost the AID-43 insert race: the first
/// <see cref="GetBySubjectAsync"/> call (the one made before attempting the
/// insert) reports "not found", <see cref="TryAddAsync"/> always reports
/// <see cref="UserAddOutcome.AlreadyExists"/>, and the caller's follow-up
/// re-read returns whatever the test configures - either the user the "other
/// caller" committed, or null to exercise the defensive-throw guard in
/// <see cref="CentralLogin.Application.Users.UserProvisioningService"/>.
/// </summary>
public sealed class RacingApplicationUserRepository : IApplicationUserRepository
{
    private readonly ApplicationUser? _userOnRetry;
    private int _getBySubjectCalls;

    public RacingApplicationUserRepository(ApplicationUser? userOnRetry) => _userOnRetry = userOnRetry;

    public int SaveChangesCallCount { get; private set; }

    public Task<ApplicationUser?> GetBySubjectAsync(KeycloakSubject subject, CancellationToken cancellationToken = default)
    {
        _getBySubjectCalls++;
        return Task.FromResult(_getBySubjectCalls == 1 ? null : _userOnRetry);
    }

    public Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Provisioning a possibly-new user must use TryAddAsync, not AddAsync.");

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }

    public Task<UserAddOutcome> TryAddAsync(ApplicationUser user, CancellationToken cancellationToken = default)
        => Task.FromResult(UserAddOutcome.AlreadyExists);
}
