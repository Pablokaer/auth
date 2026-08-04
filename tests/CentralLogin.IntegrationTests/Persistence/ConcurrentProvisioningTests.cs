using CentralLogin.Application.Users;
using CentralLogin.Domain.Users;
using CentralLogin.Infrastructure.Persistence;
using CentralLogin.Infrastructure.Persistence.Repositories;
using CentralLogin.Infrastructure.Time;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralLogin.IntegrationTests.Persistence;

/// <summary>
/// Regression coverage for AID-43: two concurrent first logins for the same,
/// previously unknown Keycloak subject must both complete successfully and
/// leave exactly one <see cref="ApplicationUser"/> row behind, rather than one
/// of them throwing an unhandled <see cref="DbUpdateException"/> from the
/// unique-index collision. Unlike <see cref="SqliteApplicationUserRepositoryTests"/>,
/// this drives the race through <see cref="UserProvisioningService"/> itself.
/// </summary>
public sealed class ConcurrentProvisioningTests : IDisposable
{
    private readonly string _directory;
    private readonly string _connectionString;

    public ConcurrentProvisioningTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "centrallogin-concurrent-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "test.db"),
            ForeignKeys = true,
        }.ToString();

        // Migrate once up-front, then let each concurrent caller open its own
        // DbContext/connection against the same, already-migrated file.
        using var migrationContext = CreateDbContext();
        migrationContext.Database.Migrate();
    }

    [Fact]
    public async Task Concurrent_first_logins_for_the_same_subject_yield_exactly_one_user()
    {
        var subject = "concurrent-sub-" + Guid.NewGuid().ToString("N");
        using var notFoundBarrier = new Barrier(2);

        using var dbContextA = CreateDbContext();
        using var dbContextB = CreateDbContext();

        var serviceA = CreateService(dbContextA, notFoundBarrier);
        var serviceB = CreateService(dbContextB, notFoundBarrier);

        // Task.Run forces each call onto its own thread-pool thread right away.
        // Without it, the async state machine can run synchronously past the
        // first await (SQLite reads often complete without a real suspension),
        // so the second call would never even start until the barrier inside
        // the first one times out - defeating the whole point of the barrier.
        var callA = Task.Run(() => serviceA.ProvisionOrUpdateAsync(
            new ProvisionUserCommand(subject, "Caller A", "a@example.com")));
        var callB = Task.Run(() => serviceB.ProvisionOrUpdateAsync(
            new ProvisionUserCommand(subject, "Caller B", "b@example.com")));

        // Neither call should throw the raw DbUpdateException from the unique
        // index collision.
        var results = await Task.WhenAll(callA, callB);

        Assert.Equal(1, results.Count(r => r.WasCreated));
        Assert.Equal(1, results.Count(r => !r.WasCreated));
        Assert.All(results, r => Assert.Equal(subject, r.Subject));

        using var verifyContext = CreateDbContext();
        var matchingSubject = KeycloakSubject.Create(subject);
        var count = await verifyContext.Users.CountAsync(u => u.KeycloakSubject == matchingSubject);
        Assert.Equal(1, count);
    }

    private CentralLoginDbContext CreateDbContext()
    {
        // Same reliability pragmas (WAL + busy_timeout) as production, so two
        // real connections writing at once wait for each other's transaction
        // instead of failing outright with SQLITE_BUSY.
        var options = new DbContextOptionsBuilder<CentralLoginDbContext>()
            .UseSqlite(_connectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        return new CentralLoginDbContext(options);
    }

    private static UserProvisioningService CreateService(CentralLoginDbContext dbContext, Barrier notFoundBarrier)
    {
        var repository = new RaceBarrierApplicationUserRepository(
            new ApplicationUserRepository(dbContext),
            notFoundBarrier);

        return new UserProvisioningService(
            repository,
            new SystemClock(),
            new ProvisionUserCommandValidator(),
            NullLogger<UserProvisioningService>.Instance);
    }

    public void Dispose()
    {
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
