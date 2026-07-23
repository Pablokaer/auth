using CentralLogin.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CentralLogin.Infrastructure.Persistence;

/// <summary>
/// EF Core context for application-specific data. Deliberately small: it holds
/// only the local <see cref="ApplicationUser"/> projection. No identity secrets.
/// </summary>
public sealed class CentralLoginDbContext : DbContext
{
    public CentralLoginDbContext(DbContextOptions<CentralLoginDbContext> options)
        : base(options)
    {
    }

    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CentralLoginDbContext).Assembly);
    }
}
