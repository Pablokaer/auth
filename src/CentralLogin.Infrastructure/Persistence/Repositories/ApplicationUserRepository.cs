using CentralLogin.Application.Abstractions;
using CentralLogin.Domain.Users;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Lets CentralLogin.UnitTests exercise IsKeycloakSubjectUniqueViolation directly
// with constructed provider exceptions, instead of needing a real database.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CentralLogin.UnitTests")]

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

    public async Task<UserAddOutcome> TryAddAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        await _dbContext.Users.AddAsync(user, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return UserAddOutcome.Added;
        }
        catch (DbUpdateException ex) when (IsKeycloakSubjectUniqueViolation(ex))
        {
            // A concurrent caller committed a row for the same subject first.
            // Detach the rejected entity so the context is left clean for the
            // caller's follow-up GetBySubjectAsync re-read.
            _dbContext.Entry(user).State = EntityState.Detached;
            return UserAddOutcome.AlreadyExists;
        }
    }

    /// <summary>
    /// True only for the SQLite or PostgreSQL UNIQUE-constraint violation on
    /// the <c>ApplicationUsers.KeycloakSubject</c> column (i.e. the
    /// <c>IX_ApplicationUsers_KeycloakSubject</c> unique index), whichever
    /// provider is configured. Internal (rather than private) so unit tests
    /// can feed constructed provider exceptions through it without a live
    /// database. Any other <see cref="DbUpdateException"/> (e.g. a NOT NULL
    /// failure or a real connectivity problem) is left to propagate.
    /// </summary>
    internal static bool IsKeycloakSubjectUniqueViolation(DbUpdateException ex)
        => IsSqliteKeycloakSubjectUniqueViolation(ex) || IsPostgresKeycloakSubjectUniqueViolation(ex);

    /// <summary>
    /// SQLite error 19 (<c>SQLITE_CONSTRAINT</c>) is generic - a NOT NULL
    /// failure on the same column produces an identical-looking
    /// "Table.Column" message, so this checks the UNIQUE-specific extended
    /// code (2067) *and* the exact message text.
    /// </summary>
    private static bool IsSqliteKeycloakSubjectUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 } sqlite
           && sqlite.Message.Contains("UNIQUE constraint failed: ApplicationUsers.KeycloakSubject", StringComparison.Ordinal);

    /// <summary>
    /// Npgsql surfaces every constraint violation with the same SQL state
    /// (<c>23505</c>, <see cref="PostgresErrorCodes.UniqueViolation"/>), so
    /// this also checks the specific constraint name - otherwise a unique
    /// violation on an unrelated column/table would be misread as the same
    /// race this repository already handles.
    /// </summary>
    private static bool IsPostgresKeycloakSubjectUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
           && postgres.ConstraintName == "IX_ApplicationUsers_KeycloakSubject";
}
