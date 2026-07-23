using FluentValidation;

namespace CentralLogin.Application.Users;

public sealed class ProvisionUserCommandValidator : AbstractValidator<ProvisionUserCommand>
{
    public ProvisionUserCommandValidator()
    {
        RuleFor(x => x.Subject)
            .NotEmpty()
            .WithMessage("The Keycloak subject claim is required.");

        RuleFor(x => x.DisplayName)
            .MaximumLength(256);

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Email snapshot must be a valid email address when present.");
    }
}
