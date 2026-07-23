using CentralLogin.Domain.Common;
using CentralLogin.Domain.Users;
using Xunit;

namespace CentralLogin.UnitTests.Domain;

public sealed class ApplicationUserTests
{
    private static readonly DateTime Now = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void Provision_sets_subject_and_timestamps()
    {
        var user = ApplicationUser.Provision(KeycloakSubject.Create("sub-1"), "Ada", "ada@example.com", Now);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("sub-1", user.KeycloakSubject.Value);
        Assert.Equal("Ada", user.DisplayName);
        Assert.Equal("ada@example.com", user.EmailSnapshot);
        Assert.Equal(Now, user.CreatedAtUtc);
        Assert.Equal(Now, user.LastLoginAtUtc);
    }

    [Fact]
    public void RecordLogin_updates_snapshots_and_last_login_but_not_created()
    {
        var user = ApplicationUser.Provision(KeycloakSubject.Create("sub-1"), "Ada", "ada@old.com", Now);
        var later = Now.AddDays(1);

        user.RecordLogin("Ada Lovelace", "ada@new.com", later);

        Assert.Equal("Ada Lovelace", user.DisplayName);
        Assert.Equal("ada@new.com", user.EmailSnapshot);
        Assert.Equal(later, user.LastLoginAtUtc);
        Assert.Equal(Now, user.CreatedAtUtc);
    }

    [Fact]
    public void RecordLogin_keeps_previous_values_when_new_ones_are_blank()
    {
        var user = ApplicationUser.Provision(KeycloakSubject.Create("sub-1"), "Ada", "ada@example.com", Now);

        user.RecordLogin("   ", null, Now.AddHours(1));

        Assert.Equal("Ada", user.DisplayName);
        Assert.Equal("ada@example.com", user.EmailSnapshot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void KeycloakSubject_Create_rejects_empty(string value)
        => Assert.Throws<DomainException>(() => KeycloakSubject.Create(value));
}
