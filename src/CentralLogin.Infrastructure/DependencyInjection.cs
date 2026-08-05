using CentralLogin.Application.Abstractions;
using CentralLogin.Infrastructure.Persistence;
using CentralLogin.Infrastructure.Persistence.Repositories;
using CentralLogin.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CentralLogin.Infrastructure;

/// <summary>Composition root for the Infrastructure layer (EF Core + SQLite/PostgreSQL).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bound through the options system (rather than a one-shot Get<T>())
        // so DatabaseOptionsValidator + ValidateOnStart abort startup on a
        // deployment that selects a provider without the configuration it
        // needs (see DatabaseOptionsValidator).
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IApplicationUserRepository, ApplicationUserRepository>();

        services.AddDbContext<CentralLoginDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            switch (databaseOptions.Provider)
            {
                case DatabaseProvider.Postgres:
                    // A separate assembly holds the Postgres migration set:
                    // EF Core scans a whole assembly for migrations, and the
                    // SQLite and Postgres migrations must never mix.
                    options.UseNpgsql(
                        databaseOptions.ConnectionString
                            ?? throw new InvalidOperationException(
                                $"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.ConnectionString)} is required " +
                                $"when {DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Provider)} is 'Postgres'. " +
                                $"{nameof(DatabaseOptionsValidator)} should have caught this at startup."),
                        npgsql => npgsql
                            .MigrationsAssembly("CentralLogin.Infrastructure.Postgres")
                            // A managed cloud database drops connections routinely
                            // (failover, maintenance, scaling); without this every
                            // drop surfaces as a 500 instead of a transparent retry.
                            // SQLite has no equivalent failure mode, so it is not
                            // added below.
                            .EnableRetryOnFailure());
                    break;

                case DatabaseProvider.Sqlite:
                    options
                        .UseSqlite(SqliteConnectionStringFactory.Build(databaseOptions))
                        .AddInterceptors(new SqlitePragmaInterceptor());
                    break;

                default:
                    // Exhaustive on purpose: a future DatabaseProvider value
                    // must not silently fall back to a container-local SQLite
                    // file - the whole point of this issue is that an
                    // unconfigured/unexpected provider fails loudly.
                    throw new InvalidOperationException(
                        $"Unsupported {DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Provider)} " +
                        $"value '{databaseOptions.Provider}'.");
            }
        });

        return services;
    }

    /// <summary>
    /// Ensures the data directory exists (SQLite) and applies any pending EF
    /// Core migrations. Call once during application startup. Safe to call
    /// from every replica: EF Core 10 takes a migration lock, so two
    /// replicas racing to migrate the same PostgreSQL database at boot
    /// serialize rather than corrupt each other.
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
