using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CentralLogin.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core tools (<c>dotnet ef</c>) to build a
/// context without booting the whole application. It points at a throwaway
/// design database file so migrations can be added/scaffolded offline.
/// </summary>
public sealed class CentralLoginDbContextFactory : IDesignTimeDbContextFactory<CentralLoginDbContext>
{
    public CentralLoginDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CentralLoginDbContext>()
            .UseSqlite("Data Source=centrallogin-design.db")
            .Options;

        return new CentralLoginDbContext(options);
    }
}
