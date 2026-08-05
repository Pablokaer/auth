using CentralLogin.Domain.Users;
using CentralLogin.Infrastructure.Persistence;
using CentralLogin.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CentralLogin.UnitTests.Infrastructure;

/// <summary>
/// Regression coverage for the AID-55 concurrent-provisioning race on
/// PostgreSQL: <see cref="ApplicationUserRepository.IsKeycloakSubjectUniqueViolation"/>
/// must recognise the unique-index collision on both providers, not just
/// SQLite, or <c>TryAddAsync</c> lets one of two racing first logins throw
/// instead of returning <c>AlreadyExists</c>. Exercised directly (the method
/// is <c>internal</c> for this purpose) with constructed provider exceptions,
/// so no live database is needed for either provider.
/// </summary>
public sealed class ApplicationUserRepositoryUniqueViolationTests
{
    [Fact]
    public void True_for_the_sqlite_KeycloakSubject_unique_index_violation()
    {
        var sqlite = new SqliteException(
            "SQLite Error 19: 'UNIQUE constraint failed: ApplicationUsers.KeycloakSubject'.",
            errorCode: 19,
            extendedErrorCode: 2067);

        Assert.True(ApplicationUserRepository.IsKeycloakSubjectUniqueViolation(new DbUpdateException("insert failed", sqlite)));
    }

    [Fact]
    public void False_for_a_sqlite_NOT_NULL_failure_on_the_same_column()
    {
        // Same "Table.Column" message shape, but extended code 1299
        // (SQLITE_CONSTRAINT_NOTNULL) rather than 2067 (SQLITE_CONSTRAINT_UNIQUE).
        var sqlite = new SqliteException(
            "SQLite Error 19: 'NOT NULL constraint failed: ApplicationUsers.KeycloakSubject'.",
            errorCode: 19,
            extendedErrorCode: 1299);

        Assert.False(ApplicationUserRepository.IsKeycloakSubjectUniqueViolation(new DbUpdateException("insert failed", sqlite)));
    }

    [Fact]
    public void True_for_the_postgres_KeycloakSubject_unique_index_violation()
    {
        var postgres = CreatePostgresException(PostgresErrorCodes.UniqueViolation, "IX_ApplicationUsers_KeycloakSubject");

        Assert.True(ApplicationUserRepository.IsKeycloakSubjectUniqueViolation(new DbUpdateException("insert failed", postgres)));
    }

    [Fact]
    public void False_for_a_postgres_unique_violation_on_a_different_constraint()
    {
        var postgres = CreatePostgresException(PostgresErrorCodes.UniqueViolation, "IX_SomeOtherTable_SomeOtherColumn");

        Assert.False(ApplicationUserRepository.IsKeycloakSubjectUniqueViolation(new DbUpdateException("insert failed", postgres)));
    }

    [Fact]
    public void False_for_a_postgres_NOT_NULL_failure()
    {
        var postgres = CreatePostgresException(PostgresErrorCodes.NotNullViolation, constraintName: null);

        Assert.False(ApplicationUserRepository.IsKeycloakSubjectUniqueViolation(new DbUpdateException("insert failed", postgres)));
    }

    [Fact]
    public void False_for_an_unrelated_DbUpdateException()
    {
        Assert.False(ApplicationUserRepository.IsKeycloakSubjectUniqueViolation(
            new DbUpdateException("a connectivity problem, not a constraint violation")));
    }

    [Fact]
    public void The_matched_index_name_is_still_the_one_the_model_creates()
    {
        // Both provider branches match on this literal, and the SQLite branch
        // matches on the table/column text behind it. Renaming the index in
        // ApplicationUserConfiguration without updating them would silently
        // lose the race handling on both providers, with a green suite.
        var options = new DbContextOptionsBuilder<CentralLoginDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var dbContext = new CentralLoginDbContext(options);

        var index = dbContext.Model.FindEntityType(typeof(ApplicationUser))!.GetIndexes().Single();

        Assert.Equal("IX_ApplicationUsers_KeycloakSubject", index.GetDatabaseName());
        Assert.True(index.IsUnique);
    }

    private static PostgresException CreatePostgresException(string sqlState, string? constraintName)
        => new(
            messageText: "duplicate key value violates unique constraint",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            detail: null,
            hint: null,
            position: 0,
            internalPosition: 0,
            internalQuery: null,
            where: null,
            schemaName: null,
            tableName: null,
            columnName: null,
            dataTypeName: null,
            constraintName: constraintName,
            file: null,
            line: null,
            routine: null);
}
