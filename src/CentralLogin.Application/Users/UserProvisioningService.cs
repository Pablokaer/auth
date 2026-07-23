using System.Security.Cryptography;
using System.Text;
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
        bool wasCreated = false;

        if (user is null)
        {
            user = ApplicationUser.Provision(subject, command.DisplayName, command.Email, now);
            await _repository.AddAsync(user, cancellationToken);
            wasCreated = true;
            _logger.LogInformation(
                "Provisioned new local user for subject {SubjectHash}.",
                HashForLog(subject.Value));
        }
        else
        {
            user.RecordLogin(command.DisplayName, command.Email, now);
            _logger.LogInformation("Updated local user {UserId} on login.", user.Id);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return ApplicationUserResult.From(user, wasCreated);
    }

    /// <summary>
    /// Produces a short, stable, non-reversible tag for correlating log lines
    /// without ever emitting the raw subject identifier.
    /// </summary>
    private static string HashForLog(string value)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(value), hash);
        return Convert.ToHexString(hash)[..12];
    }
}
