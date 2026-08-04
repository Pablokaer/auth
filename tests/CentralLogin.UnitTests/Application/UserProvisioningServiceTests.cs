using CentralLogin.Application.Abstractions;
using CentralLogin.Application.Users;
using CentralLogin.Domain.Users;
using CentralLogin.UnitTests.Fakes;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralLogin.UnitTests.Application;

public sealed class UserProvisioningServiceTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    private static UserProvisioningService CreateService(
        IApplicationUserRepository repository,
        FixedClock clock)
        => new(
            repository,
            clock,
            new ProvisionUserCommandValidator(),
            NullLogger<UserProvisioningService>.Instance);

    [Fact]
    public async Task Creates_a_new_local_user_on_first_login()
    {
        var repository = new InMemoryApplicationUserRepository();
        var service = CreateService(repository, new FixedClock(Now));

        var result = await service.ProvisionOrUpdateAsync(
            new ProvisionUserCommand("keycloak-sub-1", "Grace", "grace@example.com"));

        Assert.True(result.WasCreated);
        Assert.Single(repository.Users);
        Assert.Equal("keycloak-sub-1", result.Subject);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Updates_existing_user_and_keeps_the_same_identity()
    {
        var repository = new InMemoryApplicationUserRepository();
        var clock = new FixedClock(Now);
        var service = CreateService(repository, clock);

        await service.ProvisionOrUpdateAsync(new ProvisionUserCommand("sub-42", "Old Name", "old@example.com"));

        clock.UtcNow = Now.AddDays(3);
        var second = await service.ProvisionOrUpdateAsync(new ProvisionUserCommand("sub-42", "New Name", "new@example.com"));

        Assert.False(second.WasCreated);
        Assert.Single(repository.Users); // still one user
        Assert.Equal("New Name", second.DisplayName);
        Assert.Equal(Now.AddDays(3), second.LastLoginAtUtc);
    }

    [Fact]
    public async Task Uses_subject_not_email_as_the_permanent_key()
    {
        var repository = new InMemoryApplicationUserRepository();
        var service = CreateService(repository, new FixedClock(Now));

        // Same subject, different email on the second login.
        await service.ProvisionOrUpdateAsync(new ProvisionUserCommand("sub-7", "User", "first@example.com"));
        var second = await service.ProvisionOrUpdateAsync(new ProvisionUserCommand("sub-7", "User", "second@example.com"));

        Assert.Single(repository.Users);
        Assert.False(second.WasCreated);
        Assert.Equal("second@example.com", second.Email);
    }

    [Fact]
    public async Task Rejects_an_empty_subject()
    {
        var repository = new InMemoryApplicationUserRepository();
        var service = CreateService(repository, new FixedClock(Now));

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ProvisionOrUpdateAsync(new ProvisionUserCommand("", "User", "user@example.com")));

        Assert.Empty(repository.Users);
    }

    [Fact]
    public async Task Falls_back_to_the_existing_user_when_it_loses_the_insert_race()
    {
        // Simulates AID-43: this caller's GetBySubjectAsync says "not found",
        // but TryAddAsync then reports AlreadyExists because a concurrent first
        // login for the same subject committed first. The re-read must find
        // that winner's row and fall through to the update path, not throw.
        var winner = ApplicationUser.Provision(
            KeycloakSubject.Create("sub-race"), "Winner", "winner@example.com", Now.AddMinutes(-1));
        var repository = new RacingApplicationUserRepository(userOnRetry: winner);
        var service = CreateService(repository, new FixedClock(Now));

        var result = await service.ProvisionOrUpdateAsync(
            new ProvisionUserCommand("sub-race", "Loser", "loser@example.com"));

        Assert.False(result.WasCreated);
        Assert.Equal(winner.Id, result.Id);
        Assert.Equal("Loser", result.DisplayName); // RecordLogin applied from this caller's claims
        Assert.Equal(Now, result.LastLoginAtUtc);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Throws_when_the_retry_after_losing_the_race_still_finds_nothing()
    {
        // Defensive guard: TryAddAsync says AlreadyExists, but the re-read
        // still comes back empty. That combination should never happen for a
        // genuine unique-index collision, so it must surface loudly rather
        // than propagate a NullReferenceException or silently no-op.
        var repository = new RacingApplicationUserRepository(userOnRetry: null);
        var service = CreateService(repository, new FixedClock(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProvisionOrUpdateAsync(new ProvisionUserCommand("sub-vanished", "User", "user@example.com")));
    }
}
