using CentralLogin.Application.Abstractions;
using CentralLogin.Infrastructure.Persistence;
using CentralLogin.Infrastructure.Persistence.Repositories;
using CentralLogin.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CentralLogin.Infrastructure;

/// <summary>Composition root for the Infrastructure layer (EF Core + SQLite).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IApplicationUserRepository, ApplicationUserRepository>();

        services.AddDbContext<CentralLoginDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var connectionString = SqliteConnectionStringFactory.Build(databaseOptions);

            options
                .UseSqlite(connectionString)
                .AddInterceptors(new SqlitePragmaInterceptor());
        });

        return services;
    }

    /// <summary>
    /// Ensures the data directory exists and applies any pending EF Core
    /// migrations. Call once during application startup.
    /// </summary>
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CentralLoginDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
