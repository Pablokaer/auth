using CentralLogin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CentralLogin.UnitTests.Infrastructure;

/// <summary>
/// Guards the "one shared model, two migration sets" rule documented under
/// "Database &amp; migrations" in the README: <see cref="CentralLoginDbContext"/>
/// has a single model, but SQLite and PostgreSQL each keep their own migration
/// set in their own assembly, so a model change needs a migration added to
/// <em>both</em>. <see cref="DbContext.Database"/>'s
/// <c>HasPendingModelChanges()</c> compares the current model against the
/// snapshot embedded in the latest migration purely offline (no connection is
/// opened), so this fails the build the moment either provider's migrations
/// fall behind the model - without needing a live SQLite file or PostgreSQL
/// server.
/// </summary>
public sealed class CentralLoginDbContextPendingModelChangesTests
{
    [Fact]
    public void Sqlite_migrations_have_no_pending_model_changes()
    {
        var options = new DbContextOptionsBuilder<CentralLoginDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var dbContext = new CentralLoginDbContext(options);

        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Postgres_migrations_have_no_pending_model_changes()
    {
        // A placeholder connection string is enough: HasPendingModelChanges()
        // only compares the model snapshot, it never opens a connection.
        var options = new DbContextOptionsBuilder<CentralLoginDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=centrallogin-design;Username=design;Password=design",
                npgsql => npgsql.MigrationsAssembly("CentralLogin.Infrastructure.Postgres"))
            .Options;

        using var dbContext = new CentralLoginDbContext(options);

        Assert.False(dbContext.Database.HasPendingModelChanges());
    }
}
