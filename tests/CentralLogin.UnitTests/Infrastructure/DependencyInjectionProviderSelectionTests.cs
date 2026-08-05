using System.Collections.Generic;
using CentralLogin.Infrastructure;
using CentralLogin.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CentralLogin.UnitTests.Infrastructure;

/// <summary>
/// Regression coverage for the provider switch in
/// <see cref="DependencyInjection.AddInfrastructure"/>: without an assertion on
/// <c>Database.ProviderName</c>, deleting the <c>UseNpgsql</c>/<c>UseSqlite</c>
/// branches would leave every other test green (they only exercise repository
/// behaviour against whatever provider happens to be configured), silently
/// losing the provider selection this issue is about. No database connection
/// is opened by either case - resolving the context only configures the
/// provider, it does not connect.
/// </summary>
public sealed class DependencyInjectionProviderSelectionTests
{
    [Fact]
    public void Sqlite_is_the_default_provider()
    {
        using var provider = BuildServiceProvider(new Dictionary<string, string?>());

        using var dbContext = provider.GetRequiredService<CentralLoginDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", dbContext.Database.ProviderName);
    }

    [Fact]
    public void Postgres_is_selected_when_configured()
    {
        using var provider = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Postgres",
            ["Database:ConnectionString"] = "Host=db;Database=centrallogin;Username=app;Password=secret;SSL Mode=Require",
        });

        using var dbContext = provider.GetRequiredService<CentralLoginDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", dbContext.Database.ProviderName);
    }

    private static ServiceProvider BuildServiceProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }
}
