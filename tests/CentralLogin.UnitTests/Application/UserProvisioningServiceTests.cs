using CentralLogin.Application.Users;
using CentralLogin.UnitTests.Fakes;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralLogin.UnitTests.Application;

public sealed class UserProvisioningServiceTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    private static UserProvisioningService CreateService(
        InMemoryApplicationUserRepository repository,
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
}
