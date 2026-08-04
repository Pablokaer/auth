using CentralLogin.Application.Abstractions;
using CentralLogin.Domain.Users;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace CentralLogin.Application.Users;

/// <summary>
/// Default implementation of <see cref="IUserProvisioningService"/>.
///
/// On first login it creates a local user keyed by the Keycloak subject; on
/// subsequent logins it refreshes the display-name/email snapshots and the
/// last-login timestamp. The raw subject is never written to logs (it is an
/// identity identifier); a short, non-reversible hash is logged instead.
/// </summary>
public sealed class UserProvisioningService : IUserProvisioningService
{
    private readonly IApplicationUserRepository _repository;
    private readonly IClock _clock;
    private readonly IValidator<ProvisionUserCommand> _validator;
    private readonly ILogger<UserProvisioningService> _logger;

    public UserProvisioningService(
        IApplicationUserRepository repository,
        IClock clock,
        IValidator<ProvisionUserCommand> validator,
        ILogger<UserProvisioningService> logger)
    {
        _repository = repository;
        _clock = clock;
        _validator = validator;
        _logger = logger;
    }

    public async Task<ApplicationUserResult> ProvisionOrUpdateAsync(
        ProvisionUserCommand command,
        CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var subject = KeycloakSubject.Create(command.Subject);
        var now = _clock.UtcNow;

        var user = await _repository.GetBySubjectAsync(subject, cancellationToken);

        if (user is null)
        {
            var candidate = ApplicationUser.Provision(subject, command.DisplayName, command.Email, now);
            var outcome = await _repository.TryAddAsync(candidate, cancellationToken);

            if (outcome == UserAddOutcome.Added)
            {
                _logger.LogInformation(
                    "Provisioned new local user for subject {SubjectHash}.",
                    SubjectLogHash.Compute(subject.Value));
                return ApplicationUserResult.From(candidate, wasCreated: true);
            }

            // Lost the race against a concurrent first login for the same subject:
            // the unique index rejected our insert. Single bounded retry - re-read
            // the row the other caller committed and fall through to the
            // existing-user path below (no loop; a second collision would be a
            // genuine bug rather than a race, and should throw).
            user = await _repository.GetBySubjectAsync(subject, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Expected an existing ApplicationUser after a KeycloakSubject unique-index collision.");
        }

        user.RecordLogin(command.DisplayName, command.Email, now);
        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Updated local user {UserId} on login.", user.Id);

        return ApplicationUserResult.From(user, wasCreated: false);
    }
}
