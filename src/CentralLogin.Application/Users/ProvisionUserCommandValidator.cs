using CentralLogin.Domain.Users;
using FluentValidation;

namespace CentralLogin.Application.Users;

public sealed class ProvisionUserCommandValidator : AbstractValidator<ProvisionUserCommand>
{
    public ProvisionUserCommandValidator()
    {
        RuleFor(x => x.Subject)
            .NotEmpty()
            .WithMessage("The Keycloak subject claim is required.")
            .MaximumLength(ApplicationUserLimits.KeycloakSubjectMaxLength)
            .WithMessage($"The Keycloak subject claim must not exceed {ApplicationUserLimits.KeycloakSubjectMaxLength} characters.");

        RuleFor(x => x.DisplayName)
            .MaximumLength(ApplicationUserLimits.DisplayNameMaxLength);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Email snapshot must be a valid email address when present.")
            .MaximumLength(ApplicationUserLimits.EmailSnapshotMaxLength)
            .WithMessage($"Email snapshot must not exceed {ApplicationUserLimits.EmailSnapshotMaxLength} characters.");
    }
}
