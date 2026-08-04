using CentralLogin.Application.Abstractions;
using CentralLogin.Domain.Users;
using Xunit;

namespace CentralLogin.UnitTests.Fakes;

public sealed class InMemoryApplicationUserRepositoryTests
{
    [Fact]
    public async Task TryAddAsync_reports_AlreadyExists_for_a_duplicate_subject()
    {
        var repository = new InMemoryApplicationUserRepository();
        var now = DateTime.UtcNow;

        var first = await repository.TryAddAsync(
            ApplicationUser.Provision(KeycloakSubject.Create("sub-dup"), "A", null, now));
        var second = await repository.TryAddAsync(
            ApplicationUser.Provision(KeycloakSubject.Create("sub-dup"), "B", null, now));

        Assert.Equal(UserAddOutcome.Added, first);
        Assert.Equal(UserAddOutcome.AlreadyExists, second);
        Assert.Single(repository.Users); // the rejected duplicate was not added
        Assert.Equal(1, repository.SaveChangesCallCount); // only the successful add "commits"
    }
}
