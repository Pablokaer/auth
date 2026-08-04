using CentralLogin.Application.Users;
using CentralLogin.Domain.Users;
using Xunit;

namespace CentralLogin.UnitTests.Application;

public sealed class ProvisionUserCommandValidatorTests
{
    private static readonly ProvisionUserCommandValidator Validator = new();

    [Fact]
    public void Accepts_a_subject_at_the_max_length()
    {
        var subject = new string('s', ApplicationUserLimits.KeycloakSubjectMaxLength);

        var result = Validator.Validate(new ProvisionUserCommand(subject, null, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_a_subject_over_the_max_length()
    {
        var subject = new string('s', ApplicationUserLimits.KeycloakSubjectMaxLength + 1);

        var result = Validator.Validate(new ProvisionUserCommand(subject, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProvisionUserCommand.Subject));
    }

    [Fact]
    public void Accepts_an_email_at_the_max_length()
    {
        // Single '@', not at the start or end, is all the built-in EmailAddress() rule requires.
        var email = new string('a', ApplicationUserLimits.EmailSnapshotMaxLength - 2) + "@a";

        var result = Validator.Validate(new ProvisionUserCommand("sub-1", null, email));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_an_email_over_the_max_length()
    {
        var email = new string('a', ApplicationUserLimits.EmailSnapshotMaxLength - 1) + "@a";

        var result = Validator.Validate(new ProvisionUserCommand("sub-1", null, email));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ProvisionUserCommand.Email));
    }
}
