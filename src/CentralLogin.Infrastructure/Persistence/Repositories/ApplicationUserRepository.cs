using CentralLogin.Application.Abstractions;
using CentralLogin.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CentralLogin.Infrastructure.Persistence.Repositories;

public sealed class ApplicationUserRepository : IApplicationUserRepository
{
    private readonly CentralLoginDbContext _dbContext;

    public ApplicationUserRepository(CentralLoginDbContext dbContext) => _dbContext = dbContext;

    public Task<ApplicationUser?> GetBySubjectAsync(
        KeycloakSubject subject,
        CancellationToken cancellationToken = default)
        // Compare the value object directly: EF applies the value converter and
        // translates this to a parameterised WHERE on the KeycloakSubject column,
        // hitting the unique index.
        => _dbContext.Users.FirstOrDefaultAsync(u => u.KeycloakSubject == subject, cancellationToken);

    public async Task AddAsync(ApplicationUser user, CancellationToken cancellationToken = default)
        => await _dbContext.Users.AddAsync(user, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
