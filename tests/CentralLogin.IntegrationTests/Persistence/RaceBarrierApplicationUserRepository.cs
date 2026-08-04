using CentralLogin.Application.Abstractions;
using CentralLogin.Domain.Users;

namespace CentralLogin.IntegrationTests.Persistence;

/// <summary>
/// Test-only decorator that reliably reproduces the concurrent first-login race
/// window in <c>UserProvisioningService.ProvisionOrUpdateAsync</c>. A naive
/// <c>Task.WhenAll</c> of two calls against real SQLite does NOT reproduce it,
/// because SQLite serialises writes; by the time the second caller's insert
/// runs, the first has already committed, so the race is only ever exercised
/// end-to-end, not through the specific "both read not-found" window.
///
/// This decorator rendezvous both callers with a <see cref="Barrier"/> right
/// after each has observed <see cref="IApplicationUserRepository.GetBySubjectAsync"/>
/// return "not found", so both then proceed to insert against the real
/// repository/database at the same time - exactly the window the product code
/// must handle. No product code is changed for the test's benefit.
/// </summary>
public sealed class RaceBarrierApplicationUserRepository : IApplicationUserRepository
{
    private readonly IApplicationUserRepository _inner;
    private readonly Barrier _notFoundBarrier;

    public RaceBarrierApplicationUserRepository(IApplicationUserRepository inner, Barrier notFoundBarrier)
    {
        _inner = inner;
        _notFoundBarrier = notFoundBarrier;
    }

    public async Task<ApplicationUser?> GetBySubjectAsync(
        KeycloakSubject subject,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.GetBySubjectAsync(subject, cancellationToken);

        if (result is null)
        {
            // Both concurrent callers land here only on the very first, "not
            // found" read for a previously unknown subject; wait for the other
            // one before either proceeds to insert.
            var bothArrived = _notFoundBarrier.SignalAndWait(TimeSpan.FromSeconds(10), CancellationToken.None);
            if (!bothArrived)
            {
                throw new TimeoutException(
                    "The other concurrent caller never reached the rendezvous point; the race window was not exercised.");
            }
        }

        return result;
    }

    public Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default)
        => _inner.AddAsync(user, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _inner.SaveChangesAsync(cancellationToken);

    public Task<UserAddOutcome> TryAddAsync(ApplicationUser user, CancellationToken cancellationToken = default)
        => _inner.TryAddAsync(user, cancellationToken);
}
