using CentralLogin.Domain.Users;
using CentralLogin.Infrastructure.Persistence;
using CentralLogin.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CentralLogin.IntegrationTests.Persistence;

/// <summary>
/// Integration tests against a real SQLite database created from EF Core
/// migrations (not EnsureCreated), so the migration + unique index are exercised.
/// </summary>
public sealed class SqliteApplicationUserRepositoryTests : IDisposable
{
    private readonly string _directory;
    private readonly CentralLoginDbContext _dbContext;

    public SqliteApplicationUserRepositoryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "centrallogin-repo-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "test.db"),
            ForeignKeys = true,
        }.ToString();

        var options = new DbContextOptionsBuilder<CentralLoginDbContext>()
            .UseSqlite(connectionString)
            .Options;

        _dbContext = new CentralLoginDbContext(options);
        _dbContext.Database.Migrate();
    }

    [Fact]
    public async Task Add_then_get_by_subject_round_trips()
    {
        var repository = new ApplicationUserRepository(_dbContext);
        var subject = KeycloakSubject.Create("sub-roundtrip");

        await repository.AddAsync(ApplicationUser.Provision(subject, "Tester", "t@example.com", DateTime.UtcNow));
        await repository.SaveChangesAsync();

        var loaded = await repository.GetBySubjectAsync(subject);

        Assert.NotNull(loaded);
        Assert.Equal("sub-roundtrip", loaded!.KeycloakSubject.Value);
        Assert.Equal("Tester", loaded.DisplayName);
    }

    [Fact]
    public async Task Duplicate_subject_violates_unique_index()
    {
        var repository = new ApplicationUserRepository(_dbContext);
        var now = DateTime.UtcNow;

        await repository.AddAsync(ApplicationUser.Provision(KeycloakSubject.Create("dup"), "A", null, now));
        await repository.SaveChangesAsync();

        await repository.AddAsync(ApplicationUser.Provision(KeycloakSubject.Create("dup"), "B", null, now));

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChangesAsync());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
