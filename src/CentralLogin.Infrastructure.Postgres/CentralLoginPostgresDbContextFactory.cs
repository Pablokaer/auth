using CentralLogin.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CentralLogin.Infrastructure.Postgres;

/// <summary>
/// Design-time factory used by the EF Core tools (<c>dotnet ef</c>, run against
/// this project) to build a context targeting PostgreSQL, without booting the
/// whole application. Mirrors the intent of
/// <see cref="CentralLoginDbContextFactory"/> (SQLite design-time factory), but
/// points at a placeholder PostgreSQL connection string — no live database is
/// needed to add or scaffold a migration — and sets the migrations assembly to
/// this project so the PostgreSQL migration set never mixes with the SQLite one.
/// </summary>
public sealed class CentralLoginPostgresDbContextFactory : IDesignTimeDbContextFactory<CentralLoginDbContext>
{
    public CentralLoginDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CentralLoginDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=centrallogin-design;Username=design;Password=design",
                npgsql => npgsql.MigrationsAssembly(typeof(CentralLoginPostgresDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new CentralLoginDbContext(options);
    }
}
